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

#include <gp_Pnt.hxx>
#include <gp_Pnt2d.hxx>
#include <gp_Dir2d.hxx>
#include <gp_Ax2d.hxx>
#include <gp_Ax22d.hxx>
#include <gp_Circ2d.hxx>
#include <gp_Elips2d.hxx>
#include <Precision.hxx>
#include <Geom2d_Line.hxx>
#include <Geom2d_Circle.hxx>
#include <Geom2d_Ellipse.hxx>
#include <Geom2d_TrimmedCurve.hxx>
#include <GCE2d_MakeSegment.hxx>
#include <GCE2d_MakeArcOfCircle.hxx>
#include <GCE2d_MakeArcOfEllipse.hxx>
#include <Geom2dAPI_ProjectPointOnCurve.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <GeomAPI_PointsToBSpline.hxx>
#include <Geom_BSplineCurve.hxx>
#include <GeomAbs_Shape.hxx>
#include <Standard_Failure.hxx>

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
    catch (const Standard_Failure& e)
    {
        xbim_set_error("xbim_curve2d_reverse: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion
