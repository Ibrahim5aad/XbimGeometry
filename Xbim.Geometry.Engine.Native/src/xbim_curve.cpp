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
#include "xbim_curve2d.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"
#include "Geom_GradientCurve.h"

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
#include <GeomAdaptor_Curve.hxx>
#include <GCPnts_AbscissaPoint.hxx>
#include <Standard_Failure.hxx>

#pragma region Curve Helpers

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

#pragma endregion

#pragma region Curve Construction

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_destroy(XbimCurveHandle handle)
{
    if (!handle)
        return XBIM_OK; /* safe no-op */

    delete handle;
    return XBIM_OK;
}


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

#pragma endregion

#pragma region Curve Queries

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_parameters(
    XbimCurveHandle handle,
    double*         outFirst,
    double*         outLast)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_curve_parameters: null handle");
        return XBIM_INVALID_HANDLE;
    }
    if (!outFirst || !outLast)
    {
        xbim_set_error("xbim_curve_parameters: null output parameter");
        return XBIM_INVALID_ARG;
    }

    const Handle(Geom_Curve)& c = handle->curve;
    if (c.IsNull())
    {
        xbim_set_error("xbim_curve_parameters: curve is null");
        return XBIM_ERROR;
    }

    *outFirst = c->FirstParameter();
    *outLast  = c->LastParameter();
    return XBIM_OK;
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_length(
    XbimCurveHandle handle,
    double*         outLength)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_curve_length: null handle");
        return XBIM_INVALID_HANDLE;
    }
    if (!outLength)
    {
        xbim_set_error("xbim_curve_length: null output parameter");
        return XBIM_INVALID_ARG;
    }

    const Handle(Geom_Curve)& c = handle->curve;
    if (c.IsNull())
    {
        xbim_set_error("xbim_curve_length: curve is null");
        return XBIM_ERROR;
    }

    try
    {
        GeomAdaptor_Curve adaptor(c);
        *outLength = GCPnts_AbscissaPoint::Length(adaptor);
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_set_error(e.GetMessageString() ? e.GetMessageString()
                       : "xbim_curve_length: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_value(
    XbimCurveHandle handle,
    double          u,
    double*         outX, double* outY, double* outZ)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_curve_value: null handle");
        return XBIM_INVALID_HANDLE;
    }

    const Handle(Geom_Curve)& c = handle->curve;
    if (c.IsNull())
    {
        xbim_set_error("xbim_curve_value: curve is null");
        return XBIM_ERROR;
    }

    try
    {
        gp_Pnt pt = c->Value(u);
        if (outX) *outX = pt.X();
        if (outY) *outY = pt.Y();
        if (outZ) *outZ = pt.Z();
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_set_error(e.GetMessageString() ? e.GetMessageString()
                       : "xbim_curve_value: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_d1(
    XbimCurveHandle handle,
    double          u,
    double*         outPx, double* outPy, double* outPz,
    double*         outDx, double* outDy, double* outDz)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_curve_d1: null handle");
        return XBIM_INVALID_HANDLE;
    }

    const Handle(Geom_Curve)& c = handle->curve;
    if (c.IsNull())
    {
        xbim_set_error("xbim_curve_d1: curve is null");
        return XBIM_ERROR;
    }

    try
    {
        gp_Pnt pt;
        gp_Vec v1;
        c->D1(u, pt, v1);

        if (outPx) *outPx = pt.X();
        if (outPy) *outPy = pt.Y();
        if (outPz) *outPz = pt.Z();
        if (outDx) *outDx = v1.X();
        if (outDy) *outDy = v1.Y();
        if (outDz) *outDz = v1.Z();
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_set_error(e.GetMessageString() ? e.GetMessageString()
                       : "xbim_curve_d1: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_d2(
    XbimCurveHandle handle,
    double          u,
    double*         outPx,  double* outPy,  double* outPz,
    double*         outD1x, double* outD1y, double* outD1z,
    double*         outD2x, double* outD2y, double* outD2z)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_curve_d2: null handle");
        return XBIM_INVALID_HANDLE;
    }

    const Handle(Geom_Curve)& c = handle->curve;
    if (c.IsNull())
    {
        xbim_set_error("xbim_curve_d2: curve is null");
        return XBIM_ERROR;
    }

    try
    {
        gp_Pnt pt;
        gp_Vec v1, v2;
        c->D2(u, pt, v1, v2);

        if (outPx)  *outPx  = pt.X();
        if (outPy)  *outPy  = pt.Y();
        if (outPz)  *outPz  = pt.Z();
        if (outD1x) *outD1x = v1.X();
        if (outD1y) *outD1y = v1.Y();
        if (outD1z) *outD1z = v1.Z();
        if (outD2x) *outD2x = v2.X();
        if (outD2y) *outD2y = v2.Y();
        if (outD2z) *outD2z = v2.Z();
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_set_error(e.GetMessageString() ? e.GetMessageString()
                       : "xbim_curve_d2: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT int XBIM_CALL xbim_curve_is_closed(
    XbimCurveHandle handle,
    double          tolerance)
{
    if (!handle || handle->curve.IsNull())
        return XBIM_FALSE;

    try
    {
        gp_Pnt pFirst = handle->curve->Value(handle->curve->FirstParameter());
        gp_Pnt pLast  = handle->curve->Value(handle->curve->LastParameter());
        return pFirst.Distance(pLast) <= tolerance ? XBIM_TRUE : XBIM_FALSE;
    }
    catch (const Standard_Failure&)
    {
        return XBIM_FALSE;
    }
}

#pragma endregion

#pragma region Gradient Curve

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_gradient(
    XbimContextHandle   ctx,
    XbimCurve2dHandle   horizontalHandle,
    XbimCurve2dHandle   heightFunctionHandle,
    XbimCurveHandle*    outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_gradient: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!horizontalHandle || horizontalHandle->curve.IsNull())
    {
        xbim_set_error("xbim_curve_build_gradient: horizontalHandle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    if (!heightFunctionHandle || heightFunctionHandle->curve.IsNull())
    {
        xbim_set_error("xbim_curve_build_gradient: heightFunctionHandle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        Handle(Geom_GradientCurve) gradientCurve = new Geom_GradientCurve(
            horizontalHandle->curve, heightFunctionHandle->curve);

        *outHandle = xbim_curve_create_from(gradientCurve);
        if (!*outHandle)
        {
            xbim_set_error("xbim_curve_build_gradient: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_gradient");
        xbim_set_error("xbim_curve_build_gradient: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion
