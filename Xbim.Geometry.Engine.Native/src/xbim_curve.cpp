/*
 * xbim_curve.cpp
 *
 * Implements curve construction, query, and lifecycle functions via the flat C API.
 * Ports NCurveFactory methods from the C++/CLI engine:
 *   - Build 3D infinite line from origin and direction
 *   - Build 3D circle from axis placement and radius
 *   - Build 3D ellipse from axis placement and semi-axes
 *   - Build 3D B-spline curve from poles, knots, multiplicities, and degree
 *   - Destroy curve handle
 */

#include "xbim_curve.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <gp_Pnt.hxx>
#include <gp_Dir.hxx>
#include <gp_Vec.hxx>
#include <gp_Ax2.hxx>
#include <gp_Lin.hxx>
#include <gp_Circ.hxx>
#include <gp_Elips.hxx>
#include <Precision.hxx>
#include <Geom_Line.hxx>
#include <Geom_Circle.hxx>
#include <Geom_Ellipse.hxx>
#include <Geom_BSplineCurve.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <TColStd_Array1OfInteger.hxx>
#include <Standard_Failure.hxx>

/* ── Internal helpers ──────────────────────────────────────────────────── */

XbimCurveHandle xbim_curve_create_from(const Handle(Geom_Curve)& curve)
{
    if (curve.IsNull())
        return nullptr;

    auto* wrapper = new (std::nothrow) XbimCurve_;
    if (!wrapper)
        return nullptr;

    wrapper->curve = curve;
    return wrapper;
}

/* ── xbim_curve_destroy ────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_destroy(XbimCurveHandle handle)
{
    if (!handle)
        return XBIM_OK; /* safe no-op */

    delete handle;
    return XBIM_OK;
}

/* ── xbim_curve_build_line_3d ──────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_line_3d(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double dirX,    double dirY,    double dirZ,
    XbimCurveHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_line_3d: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    try
    {
        gp_Pnt origin(originX, originY, originZ);
        gp_Dir dir(dirX, dirY, dirZ);

        Handle(Geom_Line) line = new Geom_Line(origin, dir);

        *outHandle = xbim_curve_create_from(line);
        if (!*outHandle)
        {
            xbim_set_error("xbim_curve_build_line_3d: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_line_3d");
        xbim_set_error("xbim_curve_build_line_3d: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── xbim_curve_build_circle_3d ────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_circle_3d(
    XbimContextHandle ctx,
    double centerX, double centerY, double centerZ,
    double normalX, double normalY, double normalZ,
    double radius,
    XbimCurveHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_circle_3d: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (radius <= Precision::Confusion())
    {
        xbim_set_error("xbim_curve_build_circle_3d: radius must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        gp_Pnt center(centerX, centerY, centerZ);
        gp_Dir normal(normalX, normalY, normalZ);
        gp_Ax2 ax2(center, normal);

        Handle(Geom_Circle) circle = new Geom_Circle(ax2, radius);

        *outHandle = xbim_curve_create_from(circle);
        if (!*outHandle)
        {
            xbim_set_error("xbim_curve_build_circle_3d: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_circle_3d");
        xbim_set_error("xbim_curve_build_circle_3d: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── xbim_curve_build_ellipse_3d ───────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_ellipse_3d(
    XbimContextHandle ctx,
    double centerX,  double centerY,  double centerZ,
    double normalX,  double normalY,  double normalZ,
    double majorRadius, double minorRadius,
    XbimCurveHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_ellipse_3d: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (majorRadius <= Precision::Confusion() || minorRadius <= Precision::Confusion())
    {
        xbim_set_error("xbim_curve_build_ellipse_3d: radii must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        gp_Pnt center(centerX, centerY, centerZ);
        gp_Dir normal(normalX, normalY, normalZ);
        gp_Ax2 ax2(center, normal);

        /* OCCT requires majorRadius >= minorRadius for Geom_Ellipse.
         * If the caller's semi-axes are swapped, we swap them. */
        double major = majorRadius;
        double minor = minorRadius;
        if (minor > major)
            std::swap(major, minor);

        Handle(Geom_Ellipse) ellipse = new Geom_Ellipse(ax2, major, minor);

        *outHandle = xbim_curve_create_from(ellipse);
        if (!*outHandle)
        {
            xbim_set_error("xbim_curve_build_ellipse_3d: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_ellipse_3d");
        xbim_set_error("xbim_curve_build_ellipse_3d: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── xbim_curve_build_bspline ──────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_bspline(
    XbimContextHandle ctx,
    const double*     polesXYZ,
    int               numPoles,
    const double*     knots,
    int               numKnots,
    const int*        multiplicities,
    int               degree,
    const double*     weights,
    XbimCurveHandle*  outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_bspline: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!polesXYZ || numPoles < 2)
    {
        xbim_set_error("xbim_curve_build_bspline: invalid poles");
        return XBIM_INVALID_ARG;
    }
    if (!knots || numKnots < 2)
    {
        xbim_set_error("xbim_curve_build_bspline: invalid knots");
        return XBIM_INVALID_ARG;
    }
    if (!multiplicities)
    {
        xbim_set_error("xbim_curve_build_bspline: multiplicities is NULL");
        return XBIM_INVALID_ARG;
    }
    if (degree < 1)
    {
        xbim_set_error("xbim_curve_build_bspline: degree must be >= 1");
        return XBIM_INVALID_ARG;
    }

    try
    {
        /* Convert flat arrays to OCCT 1-based arrays */
        TColgp_Array1OfPnt poles(1, numPoles);
        for (int i = 0; i < numPoles; i++)
            poles.SetValue(i + 1, gp_Pnt(polesXYZ[i * 3], polesXYZ[i * 3 + 1], polesXYZ[i * 3 + 2]));

        TColStd_Array1OfReal knotArr(1, numKnots);
        for (int i = 0; i < numKnots; i++)
            knotArr.SetValue(i + 1, knots[i]);

        TColStd_Array1OfInteger multArr(1, numKnots);
        for (int i = 0; i < numKnots; i++)
            multArr.SetValue(i + 1, multiplicities[i]);

        Handle(Geom_BSplineCurve) bspline;

        if (weights)
        {
            TColStd_Array1OfReal weightArr(1, numPoles);
            for (int i = 0; i < numPoles; i++)
                weightArr.SetValue(i + 1, weights[i]);

            bspline = new Geom_BSplineCurve(poles, weightArr, knotArr, multArr, degree);
        }
        else
        {
            bspline = new Geom_BSplineCurve(poles, knotArr, multArr, degree);
        }

        *outHandle = xbim_curve_create_from(bspline);
        if (!*outHandle)
        {
            xbim_set_error("xbim_curve_build_bspline: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_bspline");
        xbim_set_error("xbim_curve_build_bspline: OCCT exception");
        return XBIM_ERROR;
    }
}
