/*
 * xbim_spiral.cpp
 *
 * Implements IFC4x3 spiral curve types (clothoid, sine, cosine, polynomial)
 * and their C API exports. Each spiral is evaluated via numerical integration
 * (Simpson's rule) and converted to a 3D B-spline approximation for use in
 * the geometry pipeline.
 *
 * C API functions:
 *   - xbim_curve_build_clothoid: Build a clothoid (Euler spiral)
 *   - xbim_curve_build_sine_spiral: Build a sine spiral
 *   - xbim_curve_build_cosine_spiral: Build a cosine spiral
 *   - xbim_curve_build_polynomial_spiral: Build a polynomial spiral (2nd/3rd/7th order)
 */

#include "xbim_spiral.h"
#include "xbim_curve.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <gp_Pnt.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <GeomAPI_PointsToBSpline.hxx>
#include <Standard_Failure.hxx>
#include <Precision.hxx>

#include <algorithm>

static inline double sign(double v) { return (v >= 0.0) ? 1.0 : -1.0; }

#pragma region XbimClothoid

XbimClothoid::XbimClothoid(const gp_Ax22d& placement, double clothoidConstant,
                           double startParam, double endParam)
    : Geom2d_Spiral(placement, startParam, endParam)
    , _clothoidConstant(clothoidConstant)
{
}

Standard_Real XbimClothoid::GetHeadingAt(Standard_Real s) const
{
    // theta(s) = s^2 / (2 * A^2) * sign(A)
    double A = _clothoidConstant;
    return (s * s) / (2.0 * A * std::abs(A));
}

Standard_Real XbimClothoid::GetCurvatureAt(Standard_Real s) const
{
    // kappa(s) = s / A^2 (linear curvature)
    double A = _clothoidConstant;
    return s / (A * std::abs(A));
}

void XbimClothoid::D0(Standard_Real U, gp_Pnt2d& P) const
{
    // Clothoid uses specialized Fresnel integral evaluation
    double x, y;
    EvaluateClothoid(U, x, y);
    P = ToPlacement(x, y);
}

void XbimClothoid::FresnelIntegrals(double t, double& C, double& S) const
{
    // Simpson's rule integration of Fresnel integrals:
    // C(t) = integral(cos(pi/2 * u^2), u=0..t)
    // S(t) = integral(sin(pi/2 * u^2), u=0..t)
    int N = std::max(_integrationSteps, 1000);
    if (N % 2 != 0) N++;

    double dt = t / N;
    double sumC = 0.0, sumS = 0.0;

    for (int i = 0; i <= N; i++)
    {
        double u = i * dt;
        double arg = (M_PI / 2.0) * u * u;

        double weight;
        if (i == 0 || i == N)
            weight = 1.0;
        else if (i % 2 != 0)
            weight = 4.0;
        else
            weight = 2.0;

        sumC += weight * std::cos(arg);
        sumS += weight * std::sin(arg);
    }

    C = (dt / 3.0) * sumC;
    S = (dt / 3.0) * sumS;
}

void XbimClothoid::EvaluateClothoid(double s, double& x, double& y) const
{
    double A = _clothoidConstant;
    double sqrtPiA = A * std::sqrt(M_PI);

    // Compute Fresnel parameter for current point
    double t = s / sqrtPiA;
    double C, S;
    FresnelIntegrals(t, C, S);

    // Compute Fresnel values at start point
    double t0 = _startParam / sqrtPiA;
    double C0, S0;
    FresnelIntegrals(t0, C0, S0);

    // Absolute position minus start position
    double dx = sqrtPiA * (C - C0);
    double dy = sqrtPiA * (S - S0);

    // Rotate by negative heading angle at startParam to align the tangent
    // at startParam with the local X axis
    double theta0 = GetHeadingAt(_startParam);
    double cosTheta = std::cos(-theta0);
    double sinTheta = std::sin(-theta0);

    x = dx * cosTheta - dy * sinTheta;
    y = dx * sinTheta + dy * cosTheta;
}

Handle(Geom_BSplineCurve) XbimClothoid::ToBSplineClothoid(int numSamplePoints) const
{
    if (numSamplePoints <= 0)
    {
        double span = std::abs(_endParam - _startParam);
        numSamplePoints = std::max(500, static_cast<int>(span * 10));
    }

    TColgp_Array1OfPnt points(1, numSamplePoints);
    TColStd_Array1OfReal params(1, numSamplePoints);

    for (int i = 0; i < numSamplePoints; i++)
    {
        double s = _startParam + (_endParam - _startParam) * i / (numSamplePoints - 1);
        gp_Pnt2d p2d;
        D0(s, p2d);
        points.SetValue(i + 1, gp_Pnt(p2d.X(), p2d.Y(), 0.0));
        params.SetValue(i + 1, s);
    }

    GeomAPI_PointsToBSpline fitter(points, params, 3, 8, GeomAbs_C2, Precision::Confusion());
    if (!fitter.IsDone())
        return Handle(Geom_BSplineCurve)();

    return fitter.Curve();
}

Handle(Geom2d_Geometry) XbimClothoid::Copy() const
{
    return new XbimClothoid(_placement, _clothoidConstant, _startParam, _endParam);
}

#pragma endregion

#pragma region XbimSineSpiral

XbimSineSpiral::XbimSineSpiral(const gp_Ax22d& placement,
                               double sineTerm, double linearTerm, double constantTerm,
                               double startParam, double endParam)
    : Geom2d_Spiral(placement, startParam, endParam)
    , _sineTerm(sineTerm)
    , _linearTerm(linearTerm)
    , _constantTerm(constantTerm)
    , _length(endParam - startParam)
{
}

Standard_Real XbimSineSpiral::GetHeadingAt(Standard_Real s) const
{
    // Heading angle = integral of curvature from 0 to s
    // theta(s) = s/C0 + 0.5*sign(L1)*(s/L1)^2 + (-L/(2*pi*S))*(cos(2*pi*s/L) - 1)

    double firstTerm = 0.0;
    if (_constantTerm != 0.0)
        firstTerm = s / _constantTerm;

    double secondTerm = 0.0;
    if (_linearTerm != 0.0)
        secondTerm = 0.5 * sign(_linearTerm) * std::pow(s / _linearTerm, 2.0);

    double thirdTerm = -1.0 * (_length / (2.0 * M_PI * _sineTerm)) *
                       (std::cos(2.0 * M_PI * s / _length) - 1.0);

    return firstTerm + secondTerm + thirdTerm;
}

Standard_Real XbimSineSpiral::GetCurvatureAt(Standard_Real s) const
{
    // kappa(s) = 1/C0 + sign(L1)/L1^2 * s + (2*pi/L) * (1/S) * cos(2*pi*s/L)
    // This is the derivative of the heading angle with respect to s.

    double firstTerm = 0.0;
    if (_constantTerm != 0.0)
        firstTerm = 1.0 / _constantTerm;

    double secondTerm = 0.0;
    if (_linearTerm != 0.0)
        secondTerm = sign(_linearTerm) * s / (_linearTerm * _linearTerm);

    double thirdTerm = (1.0 / _sineTerm) * std::sin(2.0 * M_PI * s / _length);

    return firstTerm + secondTerm + thirdTerm;
}

Handle(Geom2d_Geometry) XbimSineSpiral::Copy() const
{
    return new XbimSineSpiral(_placement, _sineTerm, _linearTerm, _constantTerm,
                              _startParam, _endParam);
}

#pragma endregion

#pragma region XbimCosineSpiral

XbimCosineSpiral::XbimCosineSpiral(const gp_Ax22d& placement,
                                   double cosineTerm, double constantTerm,
                                   double startParam, double endParam)
    : Geom2d_Spiral(placement, startParam, endParam)
    , _cosineTerm(cosineTerm)
    , _constantTerm(constantTerm)
    , _length(endParam - startParam)
{
}

Standard_Real XbimCosineSpiral::GetHeadingAt(Standard_Real s) const
{
    // theta(s) = s/C0 + (L/(pi*Ct))*sin(pi*s/L)

    double firstTerm = 0.0;
    if (_constantTerm != 0.0)
        firstTerm = s / _constantTerm;

    double secondTerm = (_length / (M_PI * _cosineTerm)) * std::sin(M_PI * s / _length);

    return firstTerm + secondTerm;
}

Standard_Real XbimCosineSpiral::GetCurvatureAt(Standard_Real s) const
{
    // kappa(s) = 1/C0 + (1/Ct)*cos(pi*s/L)
    // Derivative of heading with respect to s.

    double firstTerm = 0.0;
    if (_constantTerm != 0.0)
        firstTerm = 1.0 / _constantTerm;

    double secondTerm = (1.0 / _cosineTerm) * std::cos(M_PI * s / _length);

    return firstTerm + secondTerm;
}

Handle(Geom2d_Geometry) XbimCosineSpiral::Copy() const
{
    return new XbimCosineSpiral(_placement, _cosineTerm, _constantTerm,
                                _startParam, _endParam);
}

#pragma endregion

#pragma region XbimPolynomialSpiral

XbimPolynomialSpiral::XbimPolynomialSpiral(const gp_Ax22d& placement,
                                           const std::vector<std::optional<double>>& coefficients,
                                           double startParam, double endParam)
    : Geom2d_Spiral(placement, startParam, endParam)
    , _coefficients(coefficients)
{
    _coefficients.resize(8);
}

Standard_Real XbimPolynomialSpiral::GetHeadingAt(Standard_Real s) const
{
    return CalculateTheta(s);
}

Standard_Real XbimPolynomialSpiral::GetCurvatureAt(Standard_Real s) const
{
    // Curvature is the derivative of heading:
    // kappa(s) = sum over each coefficient term
    double kappa = 0.0;

    // A0 term: d/ds(t/A0) = 1/A0
    if (_coefficients[0].has_value())
        kappa += 1.0 / _coefficients[0].value();

    // A1 term: d/ds(sign(A1)*t^2/(2*A1^2)) = sign(A1)*t/A1^2
    if (_coefficients[1].has_value())
    {
        double A1 = _coefficients[1].value();
        kappa += sign(A1) * s / (A1 * A1);
    }

    // A2 term: d/ds(t^3/(3*A2^3)) = t^2/A2^3
    if (_coefficients[2].has_value())
    {
        double A2 = _coefficients[2].value();
        kappa += (s * s) / (A2 * A2 * A2);
    }

    // A3 term: d/ds(sign(A3)*t^4/(4*A3^4)) = sign(A3)*t^3/A3^4
    if (_coefficients[3].has_value())
    {
        double A3 = _coefficients[3].value();
        kappa += sign(A3) * std::pow(s, 3.0) / std::pow(A3, 4.0);
    }

    // A4 term: d/ds(t^5/(5*A4^5)) = t^4/A4^5
    if (_coefficients[4].has_value())
    {
        double A4 = _coefficients[4].value();
        kappa += std::pow(s, 4.0) / std::pow(A4, 5.0);
    }

    // A5 term: d/ds(sign(A5)*t^6/(6*A5^6)) = sign(A5)*t^5/A5^6
    if (_coefficients[5].has_value())
    {
        double A5 = _coefficients[5].value();
        kappa += sign(A5) * std::pow(s, 5.0) / std::pow(A5, 6.0);
    }

    // A6 term: d/ds(t^7/(7*A6^7)) = t^6/A6^7
    if (_coefficients[6].has_value())
    {
        double A6 = _coefficients[6].value();
        kappa += std::pow(s, 6.0) / std::pow(A6, 7.0);
    }

    // A7 term: d/ds(sign(A7)*t^8/(8*A7^8)) = sign(A7)*t^7/A7^8
    if (_coefficients[7].has_value())
    {
        double A7 = _coefficients[7].value();
        kappa += sign(A7) * std::pow(s, 7.0) / std::pow(A7, 8.0);
    }

    return kappa;
}

double XbimPolynomialSpiral::CalculateTheta(double t) const
{
    double theta = 0.0;

    // A0 term: theta += t / A0
    if (_coefficients[0].has_value())
    {
        double A0 = _coefficients[0].value();
        theta += t / A0;
    }

    // A1 term: theta += (A1 * t^2) / (2 * |A1|^3)  = sign(A1) * t^2 / (2 * A1^2)
    if (_coefficients[1].has_value())
    {
        double A1 = _coefficients[1].value();
        theta += sign(A1) * t * t / (2.0 * A1 * A1);
    }

    // A2 term: theta += t^3 / (3 * A2^3)
    if (_coefficients[2].has_value())
    {
        double A2 = _coefficients[2].value();
        theta += (t * t * t) / (3.0 * A2 * A2 * A2);
    }

    // A3 term: theta += (A3 * t^4) / (4 * |A3|^5)  = sign(A3) * t^4 / (4 * A3^4)
    if (_coefficients[3].has_value())
    {
        double A3 = _coefficients[3].value();
        theta += sign(A3) * std::pow(t, 4.0) / (4.0 * std::pow(A3, 4.0));
    }

    // A4 term: theta += t^5 / (5 * A4^5)
    if (_coefficients[4].has_value())
    {
        double A4 = _coefficients[4].value();
        theta += std::pow(t, 5.0) / (5.0 * std::pow(A4, 5.0));
    }

    // A5 term: theta += sign(A5) * t^6 / (6 * A5^6)
    if (_coefficients[5].has_value())
    {
        double A5 = _coefficients[5].value();
        theta += sign(A5) * std::pow(t, 6.0) / (6.0 * std::pow(A5, 6.0));
    }

    // A6 term: theta += t^7 / (7 * A6^7)
    if (_coefficients[6].has_value())
    {
        double A6 = _coefficients[6].value();
        theta += std::pow(t, 7.0) / (7.0 * std::pow(A6, 7.0));
    }

    // A7 term: theta += sign(A7) * t^8 / (8 * A7^8)
    if (_coefficients[7].has_value())
    {
        double A7 = _coefficients[7].value();
        theta += sign(A7) * std::pow(t, 8.0) / (8.0 * std::pow(A7, 8.0));
    }

    return theta;
}

Handle(Geom2d_Geometry) XbimPolynomialSpiral::Copy() const
{
    return new XbimPolynomialSpiral(_placement, _coefficients, _startParam, _endParam);
}

#pragma endregion

#pragma region C API Exports

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_clothoid(
    XbimContextHandle ctx,
    double clothoidConstant,
    double startParam, double endParam,
    double placementX, double placementY,
    double dirX, double dirY,
    XbimCurveHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_clothoid: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (std::abs(clothoidConstant) < Precision::Confusion())
    {
        xbim_set_error("xbim_curve_build_clothoid: clothoidConstant is zero or near-zero");
        return XBIM_INVALID_ARG;
    }

    try
    {
        gp_Pnt2d origin(placementX, placementY);
        gp_Dir2d refDir(dirX, dirY);
        gp_Ax2d mainAxis(origin, refDir);
        gp_Ax22d placement(mainAxis, true);

        XbimClothoid clothoid(placement, clothoidConstant, startParam, endParam);
        Handle(Geom_BSplineCurve) bspline = clothoid.ToBSpline();

        if (bspline.IsNull())
        {
            xbim_set_error("xbim_curve_build_clothoid: B-spline approximation failed");
            return XBIM_ERROR;
        }

        *outHandle = xbim_curve_create_from(bspline);
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_clothoid");
        xbim_set_error("xbim_curve_build_clothoid: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_sine_spiral(
    XbimContextHandle ctx,
    double sineTerm, double linearTerm, double constantTerm,
    double startParam, double endParam,
    double placementX, double placementY,
    double dirX, double dirY,
    XbimCurveHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_sine_spiral: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    try
    {
        gp_Pnt2d origin(placementX, placementY);
        gp_Dir2d refDir(dirX, dirY);
        gp_Ax2d mainAxis(origin, refDir);
        gp_Ax22d placement(mainAxis, true);

        XbimSineSpiral spiral(placement, sineTerm, linearTerm, constantTerm,
                              startParam, endParam);
        Handle(Geom_BSplineCurve) bspline = spiral.ToBSpline();

        if (bspline.IsNull())
        {
            xbim_set_error("xbim_curve_build_sine_spiral: B-spline approximation failed");
            return XBIM_ERROR;
        }

        *outHandle = xbim_curve_create_from(bspline);
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_sine_spiral");
        xbim_set_error("xbim_curve_build_sine_spiral: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_cosine_spiral(
    XbimContextHandle ctx,
    double cosineTerm, double constantTerm,
    double startParam, double endParam,
    double placementX, double placementY,
    double dirX, double dirY,
    XbimCurveHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_cosine_spiral: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    try
    {
        gp_Pnt2d origin(placementX, placementY);
        gp_Dir2d refDir(dirX, dirY);
        gp_Ax2d mainAxis(origin, refDir);
        gp_Ax22d placement(mainAxis, true);

        XbimCosineSpiral spiral(placement, cosineTerm, constantTerm,
                                startParam, endParam);
        Handle(Geom_BSplineCurve) bspline = spiral.ToBSpline();

        if (bspline.IsNull())
        {
            xbim_set_error("xbim_curve_build_cosine_spiral: B-spline approximation failed");
            return XBIM_ERROR;
        }

        *outHandle = xbim_curve_create_from(bspline);
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_cosine_spiral");
        xbim_set_error("xbim_curve_build_cosine_spiral: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_polynomial_spiral(
    XbimContextHandle ctx,
    const double* coefficients,
    const int* coefficientPresent,
    int numCoefficients,
    double startParam, double endParam,
    double placementX, double placementY,
    double dirX, double dirY,
    XbimCurveHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_polynomial_spiral: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!coefficients || !coefficientPresent || numCoefficients < 1 || numCoefficients > 8)
    {
        xbim_set_error("xbim_curve_build_polynomial_spiral: invalid coefficient data");
        return XBIM_INVALID_ARG;
    }

    try
    {
        gp_Pnt2d origin(placementX, placementY);
        gp_Dir2d refDir(dirX, dirY);
        gp_Ax2d mainAxis(origin, refDir);
        gp_Ax22d placement(mainAxis, true);

        // Build optional coefficient vector
        std::vector<std::optional<double>> coeffs(8);
        for (int i = 0; i < numCoefficients && i < 8; i++)
        {
            if (coefficientPresent[i])
                coeffs[i] = coefficients[i];
        }

        XbimPolynomialSpiral spiral(placement, coeffs, startParam, endParam);
        Handle(Geom_BSplineCurve) bspline = spiral.ToBSpline();

        if (bspline.IsNull())
        {
            xbim_set_error("xbim_curve_build_polynomial_spiral: B-spline approximation failed");
            return XBIM_ERROR;
        }

        *outHandle = xbim_curve_create_from(bspline);
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_polynomial_spiral");
        xbim_set_error("xbim_curve_build_polynomial_spiral: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion
