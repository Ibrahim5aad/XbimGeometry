/*
 * xbim_curve2d.cpp
 *
 * Implements 2D curve construction, query, and lifecycle functions via the flat C API.
 * Ports NCurveFactory 2D methods from the legacy C++/CLI engine:
 *   - Build 2D line segment from two points
 *   - Build 2D circle from center, radius, and reference direction
 *   - Build 2D ellipse from center, semi-axes, and reference direction
 *   - Build trimmed 2D curve from basis curve and parameters
 *   - Build arc of circle from parameters or 3 points
 *   - Build arc of ellipse from parameters
 *   - Project 2D point onto curve to get parameter
 *   - Reverse curve direction
 *   - Destroy curve handle
 */

#include "xbim_curve2d.h"
#include "xbim_curve.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"
#include "Geom2d_Polynomial.h"
#include "Geom2d_Spiral.h"

#include <gp_Pnt.hxx>
#include <gp_Pnt2d.hxx>
#include <gp_Dir2d.hxx>
#include <gp_Ax2d.hxx>
#include <gp_Ax22d.hxx>
#include <gp_Circ2d.hxx>
#include <gp_Elips2d.hxx>
#include <gp_Trsf2d.hxx>
#include <Precision.hxx>
#include <Geom2d_Line.hxx>
#include <Geom2d_Circle.hxx>
#include <Geom2d_Ellipse.hxx>
#include <Geom2d_TrimmedCurve.hxx>
#include <Geom2d_BSplineCurve.hxx>
#include <Geom2d_BoundedCurve.hxx>
#include <GCE2d_MakeSegment.hxx>
#include <GCE2d_MakeArcOfCircle.hxx>
#include <GCE2d_MakeArcOfEllipse.hxx>
#include <Geom2dAdaptor_Curve.hxx>
#include <Geom2dAPI_ProjectPointOnCurve.hxx>
#include <Geom2dAPI_PointsToBSpline.hxx>
#include <GCPnts_AbscissaPoint.hxx>
#include <Geom2dConvert_CompCurveToBSplineCurve.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TColgp_Array1OfPnt2d.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <TColStd_Array1OfInteger.hxx>
#include <GeomAPI_PointsToBSpline.hxx>
#include <Geom_BSplineCurve.hxx>
#include <GeomAbs_Shape.hxx>
#include <Standard_Failure.hxx>
#include <Geom2d_OffsetCurve.hxx>

#include <algorithm>
#include <cmath>

#pragma region Curve2d Helpers

XbimCurve2dHandle xbim_curve2d_create_from(const Handle(Geom2d_Curve)& curve)
{
    if (curve.IsNull())
        return nullptr;

    auto* wrapper = new (std::nothrow) XbimCurve2d_;
    if (!wrapper)
        return nullptr;

    wrapper->curve = curve;
    return wrapper;
}

#pragma endregion

#pragma region Curve2d Lifecycle

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_destroy(XbimCurve2dHandle handle)
{
    if (!handle)
        return XBIM_OK; /* safe no-op */

    delete handle;
    return XBIM_OK;
}

#pragma endregion

#pragma region Curve2d Construction

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_line(
    XbimContextHandle ctx,
    double x1, double y1,
    double x2, double y2,
    XbimCurve2dHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve2d_build_line: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    try
    {
        gp_Pnt2d p1(x1, y1);
        gp_Pnt2d p2(x2, y2);

        if (p1.Distance(p2) < Precision::Confusion())
        {
            xbim_set_error("xbim_curve2d_build_line: degenerate segment (zero length)");
            return XBIM_NULL_SHAPE;
        }

        GCE2d_MakeSegment segMaker(p1, p2);
        if (!segMaker.IsDone())
        {
            xbim_set_error("xbim_curve2d_build_line: GCE2d_MakeSegment failed");
            return XBIM_ERROR;
        }

        *outHandle = xbim_curve2d_create_from(segMaker.Value());
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve2d_build_line");
        xbim_set_error("xbim_curve2d_build_line: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_circle(
    XbimContextHandle ctx,
    double cx, double cy,
    double radius,
    double refDirX, double refDirY,
    XbimCurve2dHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve2d_build_circle: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    try
    {
        if (radius <= 0)
        {
            xbim_set_error("xbim_curve2d_build_circle: radius must be positive");
            return XBIM_INVALID_ARG;
        }

        gp_Pnt2d center(cx, cy);
        gp_Dir2d refDir(refDirX, refDirY);
        gp_Ax2d mainAxis(center, refDir);
        gp_Ax22d ax22d(mainAxis, /* isSense */ true);

        Handle(Geom2d_Circle) circle = new Geom2d_Circle(ax22d, radius);

        *outHandle = xbim_curve2d_create_from(circle);
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve2d_build_circle");
        xbim_set_error("xbim_curve2d_build_circle: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_ellipse(
    XbimContextHandle ctx,
    double cx, double cy,
    double majorRadius, double minorRadius,
    double refDirX, double refDirY,
    XbimCurve2dHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve2d_build_ellipse: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    try
    {
        if (majorRadius <= 0 || minorRadius <= 0)
        {
            xbim_set_error("xbim_curve2d_build_ellipse: radii must be positive");
            return XBIM_INVALID_ARG;
        }

        gp_Pnt2d center(cx, cy);
        gp_Dir2d refDir(refDirX, refDirY);
        gp_Ax2d mainAxis(center, refDir);
        gp_Ax22d ax22d(mainAxis, /* isSense */ true);

        // OCCT requires majorRadius >= minorRadius; swap if needed
        double maj = majorRadius, min = minorRadius;
        if (min > maj)
            std::swap(maj, min);

        Handle(Geom2d_Ellipse) ellipse = new Geom2d_Ellipse(ax22d, maj, min);

        *outHandle = xbim_curve2d_create_from(ellipse);
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve2d_build_ellipse");
        xbim_set_error("xbim_curve2d_build_ellipse: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_trimmed(
    XbimContextHandle ctx,
    XbimCurve2dHandle basisHandle,
    double u1, double u2,
    int sense,
    XbimCurve2dHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve2d_build_trimmed: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!basisHandle || basisHandle->curve.IsNull())
    {
        xbim_set_error("xbim_curve2d_build_trimmed: basisHandle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        bool sameSense = (sense != 0);
        Handle(Geom2d_TrimmedCurve) trimmed =
            new Geom2d_TrimmedCurve(basisHandle->curve, u1, u2, sameSense, /* theAdjustPeriodic */ true);

        *outHandle = xbim_curve2d_create_from(trimmed);
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve2d_build_trimmed");
        xbim_set_error("xbim_curve2d_build_trimmed: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_arc_of_circle(
    XbimContextHandle ctx,
    XbimCurve2dHandle circleHandle,
    double u1, double u2,
    int sense,
    XbimCurve2dHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve2d_build_arc_of_circle: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!circleHandle || circleHandle->curve.IsNull())
    {
        xbim_set_error("xbim_curve2d_build_arc_of_circle: circleHandle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        Handle(Geom2d_Circle) circle =
            Handle(Geom2d_Circle)::DownCast(circleHandle->curve);
        if (circle.IsNull())
        {
            xbim_set_error("xbim_curve2d_build_arc_of_circle: handle does not contain a Geom2d_Circle");
            return XBIM_INVALID_ARG;
        }

        bool sameSense = (sense != 0);

        // Match legacy: if !sense, swap parameters
        if (!sameSense)
        {
            double tmp = u1;
            u1 = u2;
            u2 = tmp;
        }

        GCE2d_MakeArcOfCircle arcMaker(circle->Circ2d(), u1, u2, sameSense);
        if (!arcMaker.IsDone())
        {
            xbim_set_error("xbim_curve2d_build_arc_of_circle: GCE2d_MakeArcOfCircle failed");
            return XBIM_ERROR;
        }

        *outHandle = xbim_curve2d_create_from(arcMaker.Value());
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve2d_build_arc_of_circle");
        xbim_set_error("xbim_curve2d_build_arc_of_circle: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_arc_of_ellipse(
    XbimContextHandle ctx,
    XbimCurve2dHandle ellipseHandle,
    double u1, double u2,
    int sense,
    XbimCurve2dHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve2d_build_arc_of_ellipse: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!ellipseHandle || ellipseHandle->curve.IsNull())
    {
        xbim_set_error("xbim_curve2d_build_arc_of_ellipse: ellipseHandle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        Handle(Geom2d_Ellipse) ellipse =
            Handle(Geom2d_Ellipse)::DownCast(ellipseHandle->curve);
        if (ellipse.IsNull())
        {
            xbim_set_error("xbim_curve2d_build_arc_of_ellipse: handle does not contain a Geom2d_Ellipse");
            return XBIM_INVALID_ARG;
        }

        bool sameSense = (sense != 0);

        GCE2d_MakeArcOfEllipse arcMaker(ellipse->Elips2d(), u1, u2, sameSense);
        if (!arcMaker.IsDone())
        {
            xbim_set_error("xbim_curve2d_build_arc_of_ellipse: GCE2d_MakeArcOfEllipse failed");
            return XBIM_ERROR;
        }

        Handle(Geom2d_TrimmedCurve) arc = arcMaker.Value();
        // Match legacy: if !sense, reverse the result
        if (!sameSense)
            arc->Reverse();

        *outHandle = xbim_curve2d_create_from(arc);
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve2d_build_arc_of_ellipse");
        xbim_set_error("xbim_curve2d_build_arc_of_ellipse: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_arc_3pt(
    XbimContextHandle ctx,
    double x1, double y1,
    double x2, double y2,
    double x3, double y3,
    XbimCurve2dHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve2d_build_arc_3pt: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    try
    {
        gp_Pnt2d p1(x1, y1);
        gp_Pnt2d p2(x2, y2);
        gp_Pnt2d p3(x3, y3);

        // Check for coincident points
        if (p1.Distance(p2) < Precision::Confusion() ||
            p2.Distance(p3) < Precision::Confusion() ||
            p1.Distance(p3) < Precision::Confusion())
        {
            xbim_set_error("xbim_curve2d_build_arc_3pt: two or more points are coincident");
            return XBIM_INVALID_ARG;
        }

        GCE2d_MakeArcOfCircle arcMaker(p1, p2, p3);
        if (!arcMaker.IsDone())
        {
            // Points may be collinear — fall back to line segment
            GCE2d_MakeSegment segMaker(p1, p3);
            if (!segMaker.IsDone())
            {
                xbim_set_error("xbim_curve2d_build_arc_3pt: points are collinear and line fallback failed");
                return XBIM_ERROR;
            }
            *outHandle = xbim_curve2d_create_from(segMaker.Value());
            return (*outHandle) ? XBIM_OK : XBIM_ERROR;
        }

        *outHandle = xbim_curve2d_create_from(arcMaker.Value());
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve2d_build_arc_3pt");
        xbim_set_error("xbim_curve2d_build_arc_3pt: OCCT exception");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_polynomial(
    XbimContextHandle ctx,
    const double* coeffsX, int numCoeffsX,
    const double* coeffsY, int numCoeffsY,
    double placementX, double placementY,
    double dirX, double dirY,
    double firstParam, double lastParam,
    XbimCurve2dHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve2d_build_polynomial: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!coeffsX || numCoeffsX < 1 || !coeffsY || numCoeffsY < 1)
    {
        xbim_set_error("xbim_curve2d_build_polynomial: coefficient arrays must be non-null with at least 1 element");
        return XBIM_INVALID_ARG;
    }

    if (lastParam <= firstParam)
    {
        xbim_set_error("xbim_curve2d_build_polynomial: lastParam must be greater than firstParam");
        return XBIM_INVALID_ARG;
    }

    try
    {
        gp_Pnt2d origin(placementX, placementY);
        gp_Dir2d refDir(dirX, dirY);
        gp_Ax2d mainAxis(origin, refDir);
        gp_Ax22d placement(mainAxis, true);

        std::vector<Standard_Real> cx(coeffsX, coeffsX + numCoeffsX);
        std::vector<Standard_Real> cy(coeffsY, coeffsY + numCoeffsY);

        Handle(Geom2d_Polynomial) polyCurve =
            new Geom2d_Polynomial(placement, cx, cy, firstParam, lastParam);

        *outHandle = xbim_curve2d_create_from(polyCurve);
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve2d_build_polynomial");
        xbim_set_error("xbim_curve2d_build_polynomial: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_polynomial(
    XbimContextHandle ctx,
    const double* coeffsX, int numCoeffsX,
    const double* coeffsY, int numCoeffsY,
    double placementX, double placementY,
    double dirX, double dirY,
    double firstParam, double lastParam,
    XbimCurveHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_polynomial: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!coeffsX || numCoeffsX < 1 || !coeffsY || numCoeffsY < 1)
    {
        xbim_set_error("xbim_curve_build_polynomial: coefficient arrays must be non-null with at least 1 element");
        return XBIM_INVALID_ARG;
    }

    if (lastParam <= firstParam)
    {
        xbim_set_error("xbim_curve_build_polynomial: lastParam must be greater than firstParam");
        return XBIM_INVALID_ARG;
    }

    try
    {
        gp_Pnt2d origin(placementX, placementY);
        gp_Dir2d refDir(dirX, dirY);
        gp_Ax2d mainAxis(origin, refDir);
        gp_Ax22d placement(mainAxis, true);

        std::vector<Standard_Real> cx(coeffsX, coeffsX + numCoeffsX);
        std::vector<Standard_Real> cy(coeffsY, coeffsY + numCoeffsY);

        Handle(Geom2d_Polynomial) polyCurve =
            new Geom2d_Polynomial(placement, cx, cy, firstParam, lastParam);

        // Move bounded curve to origin: translate start point to (0,0) and
        // rotate initial tangent to align with X axis (matches legacy behavior)
        {
            gp_Pnt2d startPt;
            gp_Vec2d tangent;
            polyCurve->D1(firstParam, startPt, tangent);
            tangent.Normalize();

            gp_Trsf2d translation;
            translation.SetTranslation(gp_Vec2d(-startPt.X(), -startPt.Y()));

            double angle = std::atan2(tangent.Y(), tangent.X());
            gp_Trsf2d rotation;
            rotation.SetRotation(gp_Pnt2d(0.0, 0.0), -angle);

            gp_Trsf2d combined = rotation * translation;
            polyCurve->Transform(combined);
        }

        // Sample the 2D curve and fit to a 3D B-spline in the XY plane (z=0)
        double span = std::abs(lastParam - firstParam);
        int numSamples = std::max(200, static_cast<int>(span * 2) + 1);

        TColgp_Array1OfPnt points(1, numSamples);
        TColStd_Array1OfReal params(1, numSamples);

        for (int i = 0; i < numSamples; i++)
        {
            double u = firstParam + (lastParam - firstParam) * i / (numSamples - 1);
            gp_Pnt2d p2d;
            polyCurve->D0(u, p2d);
            points.SetValue(i + 1, gp_Pnt(p2d.X(), p2d.Y(), 0.0));
            params.SetValue(i + 1, u);
        }

        GeomAPI_PointsToBSpline fitter(points, params, 3, 8, GeomAbs_C2, 1.0e-10);
        if (!fitter.IsDone())
        {
            xbim_set_error("xbim_curve_build_polynomial: B-spline approximation failed");
            return XBIM_ERROR;
        }

        *outHandle = xbim_curve_create_from(fitter.Curve());
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_polynomial");
        xbim_set_error("xbim_curve_build_polynomial: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion

#pragma region Curve2d Queries

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_parameters(
    XbimCurve2dHandle handle,
    double*           outFirst,
    double*           outLast)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_curve2d_parameters: null handle");
        return XBIM_INVALID_HANDLE;
    }
    if (!outFirst || !outLast)
    {
        xbim_set_error("xbim_curve2d_parameters: null output parameter");
        return XBIM_INVALID_ARG;
    }

    const Handle(Geom2d_Curve)& c = handle->curve;
    if (c.IsNull())
    {
        xbim_set_error("xbim_curve2d_parameters: curve is null");
        return XBIM_ERROR;
    }

    *outFirst = c->FirstParameter();
    *outLast  = c->LastParameter();
    return XBIM_OK;
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_length(
    XbimCurve2dHandle handle,
    double*           outLength)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_curve2d_length: null handle");
        return XBIM_INVALID_HANDLE;
    }
    if (!outLength)
    {
        xbim_set_error("xbim_curve2d_length: null output parameter");
        return XBIM_INVALID_ARG;
    }

    const Handle(Geom2d_Curve)& c = handle->curve;
    if (c.IsNull())
    {
        xbim_set_error("xbim_curve2d_length: curve is null");
        return XBIM_ERROR;
    }

    try
    {
        Geom2dAdaptor_Curve adaptor(c);
        *outLength = GCPnts_AbscissaPoint::Length(adaptor);
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_set_error(e.GetMessageString() ? e.GetMessageString()
                       : "xbim_curve2d_length: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_value(
    XbimCurve2dHandle handle,
    double            u,
    double*           outX, double* outY)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_curve2d_value: null handle");
        return XBIM_INVALID_HANDLE;
    }

    const Handle(Geom2d_Curve)& c = handle->curve;
    if (c.IsNull())
    {
        xbim_set_error("xbim_curve2d_value: curve is null");
        return XBIM_ERROR;
    }

    try
    {
        gp_Pnt2d pt;
        c->D0(u, pt);
        if (outX) *outX = pt.X();
        if (outY) *outY = pt.Y();
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_set_error(e.GetMessageString() ? e.GetMessageString()
                       : "xbim_curve2d_value: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_d1(
    XbimCurve2dHandle handle,
    double            u,
    double*           outPx, double* outPy,
    double*           outDx, double* outDy)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_curve2d_d1: null handle");
        return XBIM_INVALID_HANDLE;
    }

    const Handle(Geom2d_Curve)& c = handle->curve;
    if (c.IsNull())
    {
        xbim_set_error("xbim_curve2d_d1: curve is null");
        return XBIM_ERROR;
    }

    try
    {
        gp_Pnt2d pt;
        gp_Vec2d v1;
        c->D1(u, pt, v1);

        if (outPx) *outPx = pt.X();
        if (outPy) *outPy = pt.Y();
        if (outDx) *outDx = v1.X();
        if (outDy) *outDy = v1.Y();
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_set_error(e.GetMessageString() ? e.GetMessageString()
                       : "xbim_curve2d_d1: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_d2(
    XbimCurve2dHandle handle,
    double            u,
    double*           outPx,  double* outPy,
    double*           outD1x, double* outD1y,
    double*           outD2x, double* outD2y)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_curve2d_d2: null handle");
        return XBIM_INVALID_HANDLE;
    }

    const Handle(Geom2d_Curve)& c = handle->curve;
    if (c.IsNull())
    {
        xbim_set_error("xbim_curve2d_d2: curve is null");
        return XBIM_ERROR;
    }

    try
    {
        gp_Pnt2d pt;
        gp_Vec2d v1, v2;
        c->D2(u, pt, v1, v2);

        if (outPx)  *outPx  = pt.X();
        if (outPy)  *outPy  = pt.Y();
        if (outD1x) *outD1x = v1.X();
        if (outD1y) *outD1y = v1.Y();
        if (outD2x) *outD2x = v2.X();
        if (outD2y) *outD2y = v2.Y();
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_set_error(e.GetMessageString() ? e.GetMessageString()
                       : "xbim_curve2d_d2: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_project_point(
    XbimContextHandle ctx,
    XbimCurve2dHandle curveHandle,
    double px, double py,
    double tolerance,
    double* outParam)
{
    xbim_clear_error();

    if (!outParam)
    {
        xbim_set_error("xbim_curve2d_project_point: outParam is NULL");
        return XBIM_INVALID_ARG;
    }
    *outParam = 0.0;

    if (!curveHandle || curveHandle->curve.IsNull())
    {
        xbim_set_error("xbim_curve2d_project_point: curveHandle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        gp_Pnt2d pnt(px, py);

        Geom2dAPI_ProjectPointOnCurve projector(pnt, curveHandle->curve);
        if (projector.NbPoints() == 0)
        {
            xbim_set_error("xbim_curve2d_project_point: point projection found no solution");
            return XBIM_ERROR;
        }

        *outParam = projector.LowerDistanceParameter();
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve2d_project_point");
        xbim_set_error("xbim_curve2d_project_point: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion

#pragma region Curve2d Mutation

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_reverse(XbimCurve2dHandle handle)
{
    xbim_clear_error();

    if (!handle || handle->curve.IsNull())
    {
        xbim_set_error("xbim_curve2d_reverse: handle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        handle->curve->Reverse();
        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_curve2d_reverse: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion

#pragma region Curve2d Transform

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_transform(
    XbimCurve2dHandle handle,
    double placementX, double placementY,
    double dirX,       double dirY)
{
    xbim_clear_error();

    if (!handle || handle->curve.IsNull())
    {
        xbim_set_error("xbim_curve2d_transform: handle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        gp_Pnt2d origin(placementX, placementY);
        gp_Dir2d xDir(dirX, dirY);
        gp_Ax2d axis(origin, xDir);
        gp_Ax2d globalAxis(gp::Origin2d(), gp_Dir2d(1, 0));

        gp_Trsf2d trsf;
        trsf.SetTransformation(axis, globalAxis);

        handle->curve->Transform(trsf);
        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_curve2d_transform: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_move_to_origin(
    XbimCurve2dHandle handle)
{
    xbim_clear_error();

    if (!handle || handle->curve.IsNull())
    {
        xbim_set_error("xbim_curve2d_move_to_origin: handle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        gp_Pnt2d startPt;
        handle->curve->D0(handle->curve->FirstParameter(), startPt);

        if (startPt.Distance(gp::Origin2d()) < Precision::Confusion())
            return XBIM_OK; /* already at origin */

        gp_Trsf2d translation;
        translation.SetTranslation(gp_Vec2d(-startPt.X(), -startPt.Y()));
        handle->curve->Transform(translation);

        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_curve2d_move_to_origin: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_align_to_origin(
    XbimCurve2dHandle handle)
{
    xbim_clear_error();

    if (!handle || handle->curve.IsNull())
    {
        xbim_set_error("xbim_curve2d_align_to_origin: handle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        gp_Pnt2d startPt;
        gp_Vec2d tangent;
        handle->curve->D1(handle->curve->FirstParameter(), startPt, tangent);
        tangent.Normalize();

        gp_Trsf2d translation;
        translation.SetTranslation(gp_Vec2d(-startPt.X(), -startPt.Y()));

        double angle = std::atan2(tangent.Y(), tangent.X());
        gp_Trsf2d rotation;
        rotation.SetRotation(gp_Pnt2d(0.0, 0.0), -angle);

        gp_Trsf2d combined = rotation * translation;
        handle->curve->Transform(combined);

        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_curve2d_align_to_origin: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_translate_start_to_x(
    XbimCurve2dHandle handle,
    double            targetX)
{
    xbim_clear_error();

    if (!handle || handle->curve.IsNull())
    {
        xbim_set_error("xbim_curve2d_translate_start_to_x: handle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        Handle(Geom2d_BSplineCurve) bspline =
            Handle(Geom2d_BSplineCurve)::DownCast(handle->curve);
        if (bspline.IsNull())
        {
            xbim_set_error("xbim_curve2d_translate_start_to_x: curve must be a B-spline");
            return XBIM_INVALID_ARG;
        }

        gp_Pnt2d startPt;
        bspline->D0(bspline->FirstParameter(), startPt);
        double dx = targetX - startPt.X();

        if (std::abs(dx) < Precision::Confusion())
            return XBIM_OK;

        /* Shift all poles in X */
        for (int i = 1; i <= bspline->NbPoles(); ++i)
        {
            gp_Pnt2d pole = bspline->Pole(i);
            bspline->SetPole(i, gp_Pnt2d(pole.X() + dx, pole.Y()));
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_curve2d_translate_start_to_x: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion

#pragma region Curve2d Composite

/*
 * Helper: approximates a bounded 2D curve to a B-spline by point sampling.
 * Used for spirals, polynomials, and conic-like curves that can't be added
 * directly to Geom2dConvert_CompCurveToBSplineCurve.
 */
static Handle(Geom2d_BSplineCurve) ApproximateCurve2d(
    const Handle(Geom2d_Curve)& curve,
    Standard_Real first, Standard_Real last,
    int numPoints)
{
    if (numPoints < 2) numPoints = 200;

    TColgp_Array1OfPnt2d points(1, numPoints);
    double delta = (last - first) / (numPoints - 1);

    for (int i = 1; i <= numPoints; ++i)
    {
        double u = first + (i - 1) * delta;
        if (u > last) u = last;
        gp_Pnt2d pt;
        curve->D0(u, pt);
        points.SetValue(i, pt);
    }

    Geom2dAPI_PointsToBSpline fitter(points, 8, 8, GeomAbs_CN);
    return fitter.Curve();
}


/*
 * Helper: determine if a 2D curve is a conic or wrapped conic (trimmed/offset).
 */
static bool IsConic2d(const Handle(Geom2d_Curve)& curve)
{
    if (curve.IsNull()) return false;

    if (!Handle(Geom2d_Circle)::DownCast(curve).IsNull()) return true;
    if (!Handle(Geom2d_Ellipse)::DownCast(curve).IsNull()) return true;

    Handle(Geom2d_TrimmedCurve) trimmed = Handle(Geom2d_TrimmedCurve)::DownCast(curve);
    if (!trimmed.IsNull())
        return IsConic2d(trimmed->BasisCurve());

    return false;
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_bspline(
    XbimContextHandle   ctx,
    const double*       polesXY,
    int                 numPoles,
    const double*       knots,
    int                 numKnots,
    const int*          multiplicities,
    int                 degree,
    const double*       weights,
    XbimCurve2dHandle*  outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve2d_build_bspline: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!polesXY || numPoles < 2)
    {
        xbim_set_error("xbim_curve2d_build_bspline: invalid poles");
        return XBIM_INVALID_ARG;
    }
    if (!knots || numKnots < 2)
    {
        xbim_set_error("xbim_curve2d_build_bspline: invalid knots");
        return XBIM_INVALID_ARG;
    }
    if (!multiplicities)
    {
        xbim_set_error("xbim_curve2d_build_bspline: multiplicities is NULL");
        return XBIM_INVALID_ARG;
    }
    if (degree < 1)
    {
        xbim_set_error("xbim_curve2d_build_bspline: degree must be >= 1");
        return XBIM_INVALID_ARG;
    }

    try
    {
        /* Convert flat arrays to OCCT 1-based arrays */
        TColgp_Array1OfPnt2d poles(1, numPoles);
        for (int i = 0; i < numPoles; i++)
            poles.SetValue(i + 1, gp_Pnt2d(polesXY[i * 2], polesXY[i * 2 + 1]));

        TColStd_Array1OfReal knotArr(1, numKnots);
        for (int i = 0; i < numKnots; i++)
            knotArr.SetValue(i + 1, knots[i]);

        TColStd_Array1OfInteger multArr(1, numKnots);
        for (int i = 0; i < numKnots; i++)
            multArr.SetValue(i + 1, multiplicities[i]);

        Handle(Geom2d_BSplineCurve) bspline;

        if (weights)
        {
            TColStd_Array1OfReal weightArr(1, numPoles);
            for (int i = 0; i < numPoles; i++)
                weightArr.SetValue(i + 1, weights[i]);

            bspline = new Geom2d_BSplineCurve(poles, weightArr, knotArr, multArr, degree);
        }
        else
        {
            bspline = new Geom2d_BSplineCurve(poles, knotArr, multArr, degree);
        }

        *outHandle = xbim_curve2d_create_from(bspline);
        if (!*outHandle)
        {
            xbim_set_error("xbim_curve2d_build_bspline: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve2d_build_bspline");
        xbim_set_error("xbim_curve2d_build_bspline: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_composite_bspline(
    XbimContextHandle   ctx,
    XbimCurve2dHandle*  curves,
    int                 numCurves,
    double              tolerance,
    XbimCurve2dHandle*  outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve2d_build_composite_bspline: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!curves || numCurves < 1)
    {
        xbim_set_error("xbim_curve2d_build_composite_bspline: need at least 1 curve");
        return XBIM_INVALID_ARG;
    }

    try
    {
        Geom2dConvert_CompCurveToBSplineCurve converter(Convert_RationalC1);

        gp_Pnt2d prevEnd;
        bool hasPrev = false;

        for (int i = 0; i < numCurves; ++i)
        {
            if (!curves[i] || curves[i]->curve.IsNull())
            {
                xbim_log_warning(ctx, "xbim_curve2d_build_composite_bspline: curve %d is NULL, skipping", i);
                continue;
            }

            Handle(Geom2d_Curve) c = curves[i]->curve;
            Handle(Geom2d_BoundedCurve) bounded = Handle(Geom2d_BoundedCurve)::DownCast(c);
            if (bounded.IsNull())
            {
                xbim_log_warning(ctx, "xbim_curve2d_build_composite_bspline: curve %d is not bounded, skipping", i);
                continue;
            }

            Standard_Real first = bounded->FirstParameter();
            Standard_Real last = bounded->LastParameter();

            /* Fill gap between segments with a line if needed */
            if (hasPrev)
            {
                gp_Pnt2d startPt;
                bounded->D0(first, startPt);
                if (!prevEnd.IsEqual(startPt, tolerance))
                {
                    gp_Dir2d gapDir(gp_Vec2d(prevEnd, startPt));
                    double gapLen = prevEnd.Distance(startPt);
                    Handle(Geom2d_TrimmedCurve) gapLine = new Geom2d_TrimmedCurve(
                        new Geom2d_Line(prevEnd, gapDir), 0.0, gapLen);
                    converter.Add(gapLine, tolerance, false);
                }
            }

            /* Try to add directly, approximate if needed */
            Handle(Geom2d_BSplineCurve) toAdd;

            Handle(Geom2d_Spiral) spiral = Handle(Geom2d_Spiral)::DownCast(bounded);
            Handle(Geom2d_Polynomial) polynomial = Handle(Geom2d_Polynomial)::DownCast(bounded);

            if (!polynomial.IsNull())
            {
                int n = std::max(1000, static_cast<int>(std::abs(last - first)));
                toAdd = ApproximateCurve2d(bounded, first, last, n);
            }
            else if (!spiral.IsNull())
            {
                int n = spiral->GetIntegrationSteps();
                toAdd = ApproximateCurve2d(bounded, first, last, n);
            }
            else if (IsConic2d(bounded))
            {
                int n = std::max(200, static_cast<int>(std::abs(last - first) * 10) + 1);
                toAdd = ApproximateCurve2d(bounded, first, last, n);
            }
            else if (!converter.Add(bounded, tolerance, false))
            {
                int n = std::max(200, static_cast<int>(std::abs(last - first) * 10) + 1);
                toAdd = ApproximateCurve2d(bounded, first, last, n);
            }

            if (!toAdd.IsNull())
            {
                if (!converter.Add(toAdd, tolerance, false))
                {
                    xbim_log_warning(ctx,
                        "xbim_curve2d_build_composite_bspline: failed to add curve %d after approximation", i);
                }
            }

            /* Track end point for gap filling */
            bounded->D0(last, prevEnd);
            hasPrev = true;
        }

        Handle(Geom2d_BSplineCurve) result = converter.BSplineCurve();
        if (result.IsNull())
        {
            xbim_set_error("xbim_curve2d_build_composite_bspline: composite B-spline is null");
            return XBIM_ERROR;
        }

        *outHandle = xbim_curve2d_create_from(result);
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve2d_build_composite_bspline");
        xbim_set_error("xbim_curve2d_build_composite_bspline: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion

#pragma region Offset Curve 2D

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_offset(
    XbimContextHandle  ctx,
    XbimCurve2dHandle  basisHandle,
    double             offset,
    XbimCurve2dHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve2d_build_offset: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!basisHandle || basisHandle->curve.IsNull())
    {
        xbim_set_error("xbim_curve2d_build_offset: basisHandle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        Handle(Geom2d_OffsetCurve) offsetCurve =
            new Geom2d_OffsetCurve(basisHandle->curve, offset);

        if (offsetCurve.IsNull())
        {
            xbim_set_error("xbim_curve2d_build_offset: resulting offset curve is null");
            return XBIM_ERROR;
        }

        *outHandle = xbim_curve2d_create_from(offsetCurve);
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve2d_build_offset");
        xbim_set_error("xbim_curve2d_build_offset: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion
