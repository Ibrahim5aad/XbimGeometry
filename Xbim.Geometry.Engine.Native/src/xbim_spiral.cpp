/*
 * xbim_spiral.cpp
 *
 * C API exports for IFC4x3 spiral curve types (clothoid, sine, cosine,
 * polynomial). Each spiral is evaluated via numerical integration
 * (Simpson's rule) and converted to a 3D B-spline approximation for use
 * in the geometry pipeline.
 *
 * C API functions:
 *   - xbim_curve_build_clothoid: Build a clothoid (Euler spiral)
 *   - xbim_curve_build_sine_spiral: Build a sine spiral
 *   - xbim_curve_build_cosine_spiral: Build a cosine spiral
 *   - xbim_curve_build_polynomial_spiral: Build a polynomial spiral (2nd/3rd/7th order)
 */

#include "xbim_spiral.h"
#include "Geom2d_Clothoid.h"
#include "Geom2d_CosineSpiral.h"
#include "Geom2d_SineSpiral.h"
#include "Geom2d_PolynomialSpiral.h"
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

        Geom2d_Clothoid clothoid(placement, clothoidConstant, startParam, endParam);
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

        Geom2d_SineSpiral spiral(placement, sineTerm, linearTerm, constantTerm,
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

        Geom2d_CosineSpiral spiral(placement, cosineTerm, constantTerm,
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

        Geom2d_PolynomialSpiral spiral(placement, coeffs, startParam, endParam);
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
