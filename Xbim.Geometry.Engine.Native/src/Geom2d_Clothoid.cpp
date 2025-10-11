#include "Geom2d_Clothoid.h"

#include <gp_Pnt.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <GeomAPI_PointsToBSpline.hxx>
#include <Precision.hxx>

#include <algorithm>
#include <cmath>

Geom2d_Clothoid::Geom2d_Clothoid(const gp_Ax22d& placement, double clothoidConstant,
                                   double startParam, double endParam)
    : Geom2d_Spiral(placement, startParam, endParam)
    , _clothoidConstant(clothoidConstant)
{
}

Standard_Real Geom2d_Clothoid::GetHeadingAt(Standard_Real s) const
{
    // theta(s) = s^2 / (2 * A^2) * sign(A)
    double A = _clothoidConstant;
    return (s * s) / (2.0 * A * std::abs(A));
}

Standard_Real Geom2d_Clothoid::GetCurvatureAt(Standard_Real s) const
{
    // kappa(s) = s / A^2 (linear curvature)
    double A = _clothoidConstant;
    return s / (A * std::abs(A));
}

void Geom2d_Clothoid::D0(Standard_Real U, gp_Pnt2d& P) const
{
    // Clothoid uses specialized Fresnel integral evaluation
    double x, y;
    EvaluateClothoid(U, x, y);
    P = ToPlacement(x, y);
}

void Geom2d_Clothoid::FresnelIntegrals(double t, double& C, double& S) const
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

void Geom2d_Clothoid::EvaluateClothoid(double s, double& x, double& y) const
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

Handle(Geom_BSplineCurve) Geom2d_Clothoid::ToBSplineClothoid(int numSamplePoints) const
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

Handle(Geom2d_Geometry) Geom2d_Clothoid::Copy() const
{
    return new Geom2d_Clothoid(_placement, _clothoidConstant, _startParam, _endParam);
}
