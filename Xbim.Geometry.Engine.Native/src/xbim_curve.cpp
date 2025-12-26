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
#include "xbim_location.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"
#include "Geom_GradientCurve.h"
#include "Geom_SegmentedReferenceCurve.h"
#include "Geom_EllipseWithSemiAxes.h"

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
#include <GeomConvert_CompCurveToBSplineCurve.hxx>
#include <GeomAPI_PointsToBSpline.hxx>
#include <Geom_BoundedCurve.hxx>
#include <Geom_TrimmedCurve.hxx>
#include <GC_MakeArcOfCircle.hxx>
#include <GC_MakeArcOfEllipse.hxx>
#include <GC_MakeCircle.hxx>
#include <GC_MakeSegment.hxx>
#include <Standard_Failure.hxx>
#include <GeomLib_Tool.hxx>
#include <Geom_OffsetCurve.hxx>

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
    double xDirX,   double xDirY,   double xDirZ,
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
        gp_Dir xDir(xDirX, xDirY, xDirZ);
        gp_Ax2 ax2(center, normal, xDir);

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
    double xDirX,    double xDirY,    double xDirZ,
    double semiAxis1, double semiAxis2,
    int* outRotated,
    XbimCurveHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_ellipse_3d: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (semiAxis1 <= Precision::Confusion() || semiAxis2 <= Precision::Confusion())
    {
        xbim_set_error("xbim_curve_build_ellipse_3d: semi-axes must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        gp_Pnt center(centerX, centerY, centerZ);
        gp_Dir normal(normalX, normalY, normalZ);
        gp_Dir xDir(xDirX, xDirY, xDirZ);
        gp_Ax2 ax2(center, normal, xDir);

        Handle(Geom_EllipseWithSemiAxes) ellipse =
            new Geom_EllipseWithSemiAxes(ax2, semiAxis1, semiAxis2);

        if (outRotated)
            *outRotated = ellipse->IsRotated() ? 1 : 0;

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


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_trimmed_3d(
    XbimContextHandle ctx,
    XbimCurveHandle   basisHandle,
    double u1, double u2,
    int sense,
    XbimCurveHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_trimmed_3d: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!basisHandle || basisHandle->curve.IsNull())
    {
        xbim_set_error("xbim_curve_build_trimmed_3d: basisHandle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        bool sameSense = (sense != 0);
        Handle(Geom_Curve) basis = basisHandle->curve;

        /* Circle: use GC_MakeArcOfCircle for proper arc construction */
        Handle(Geom_Circle) circle = Handle(Geom_Circle)::DownCast(basis);
        if (!circle.IsNull())
        {
            if (!sameSense)
            {
                double tmp = u1;
                u1 = u2;
                u2 = tmp;
            }
            GC_MakeArcOfCircle arcMaker(circle->Circ(), u1, u2, sameSense);
            if (!arcMaker.IsDone())
            {
                xbim_set_error("xbim_curve_build_trimmed_3d: GC_MakeArcOfCircle failed");
                return XBIM_ERROR;
            }
            *outHandle = xbim_curve_create_from(arcMaker.Value());
            return (*outHandle) ? XBIM_OK : XBIM_ERROR;
        }

        /* Ellipse with semi-axes: convert IFC trim parameters, then arc */
        Handle(Geom_EllipseWithSemiAxes) ellipse =
            Handle(Geom_EllipseWithSemiAxes)::DownCast(basis);
        if (!ellipse.IsNull())
        {
            u1 = ellipse->ConvertIfcTrimParameter(u1);
            u2 = ellipse->ConvertIfcTrimParameter(u2);
            GC_MakeArcOfEllipse arcMaker(ellipse->Elips(), u1, u2, sameSense);
            if (!arcMaker.IsDone())
            {
                xbim_set_error("xbim_curve_build_trimmed_3d: GC_MakeArcOfEllipse failed");
                return XBIM_ERROR;
            }
            Handle(Geom_TrimmedCurve) arc = arcMaker.Value();
            if (!sameSense)
                arc->Reverse();
            *outHandle = xbim_curve_create_from(arc);
            return (*outHandle) ? XBIM_OK : XBIM_ERROR;
        }

        /* Generic: Geom_TrimmedCurve with adjustable periodicity */
        Handle(Geom_TrimmedCurve) trimmed =
            new Geom_TrimmedCurve(basis, u1, u2, sameSense, /* theAdjustPeriodic */ true);

        *outHandle = xbim_curve_create_from(trimmed);
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_trimmed_3d");
        xbim_set_error("xbim_curve_build_trimmed_3d: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_trimmed_line_3d(
    XbimContextHandle ctx,
    double x1, double y1, double z1,
    double x2, double y2, double z2,
    XbimCurveHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_trimmed_line_3d: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    try
    {
        gp_Pnt start(x1, y1, z1);
        gp_Pnt end(x2, y2, z2);
        gp_Vec dir(start, end);

        if (dir.Magnitude() < Precision::Confusion())
        {
            xbim_set_error("xbim_curve_build_trimmed_line_3d: start and end points are identical");
            return XBIM_INVALID_ARG;
        }

        gp_Lin line(start, dir);
        Handle(Geom_Line) hLine = new Geom_Line(line);
        Handle(Geom_TrimmedCurve) trimmed =
            new Geom_TrimmedCurve(hLine, 0.0, dir.Magnitude());

        *outHandle = xbim_curve_create_from(trimmed);
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_trimmed_line_3d");
        xbim_set_error("xbim_curve_build_trimmed_line_3d: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_circle_3pt_3d(
    XbimContextHandle ctx,
    double x1, double y1, double z1,
    double x2, double y2, double z2,
    double x3, double y3, double z3,
    XbimCurveHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_circle_3pt_3d: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    try
    {
        gp_Pnt p1(x1, y1, z1);
        gp_Pnt p2(x2, y2, z2);
        gp_Pnt p3(x3, y3, z3);

        /* Check for coincident points */
        if (p1.Distance(p2) < Precision::Confusion() ||
            p2.Distance(p3) < Precision::Confusion() ||
            p1.Distance(p3) < Precision::Confusion())
        {
            xbim_set_error("xbim_curve_build_circle_3pt_3d: two or more points are coincident");
            return XBIM_INVALID_ARG;
        }

        GC_MakeCircle circleMaker(p1, p2, p3);
        if (!circleMaker.IsDone())
        {
            /* Points are likely collinear — caller should handle fallback */
            xbim_set_error("xbim_curve_build_circle_3pt_3d: circle could not be built from 3 points (collinear?)");
            return XBIM_ERROR;
        }

        *outHandle = xbim_curve_create_from(circleMaker.Value());
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_circle_3pt_3d");
        xbim_set_error("xbim_curve_build_circle_3pt_3d: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_arc_of_circle_3d(
    XbimContextHandle ctx,
    XbimCurveHandle   circleHandle,
    double u1, double u2,
    int sense,
    XbimCurveHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_arc_of_circle_3d: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!circleHandle || circleHandle->curve.IsNull())
    {
        xbim_set_error("xbim_curve_build_arc_of_circle_3d: circleHandle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        Handle(Geom_Circle) circle =
            Handle(Geom_Circle)::DownCast(circleHandle->curve);
        if (circle.IsNull())
        {
            xbim_set_error("xbim_curve_build_arc_of_circle_3d: handle does not contain a Geom_Circle");
            return XBIM_INVALID_ARG;
        }

        bool sameSense = (sense != 0);

        /* Match legacy: if !sense, swap parameters */
        if (!sameSense)
        {
            double tmp = u1;
            u1 = u2;
            u2 = tmp;
        }

        GC_MakeArcOfCircle arcMaker(circle->Circ(), u1, u2, sameSense);
        if (!arcMaker.IsDone())
        {
            xbim_set_error("xbim_curve_build_arc_of_circle_3d: GC_MakeArcOfCircle failed");
            return XBIM_ERROR;
        }

        *outHandle = xbim_curve_create_from(arcMaker.Value());
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_arc_of_circle_3d");
        xbim_set_error("xbim_curve_build_arc_of_circle_3d: OCCT exception");
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


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_parameter_at_length(
    XbimCurveHandle handle,
    double          arcLength,
    double          tolerance,
    double*         outParameter)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_curve_parameter_at_length: null handle");
        return XBIM_INVALID_HANDLE;
    }
    if (!outParameter)
    {
        xbim_set_error("xbim_curve_parameter_at_length: null output parameter");
        return XBIM_INVALID_ARG;
    }

    const Handle(Geom_Curve)& c = handle->curve;
    if (c.IsNull())
    {
        xbim_set_error("xbim_curve_parameter_at_length: curve is null");
        return XBIM_ERROR;
    }

    try
    {
        GeomAdaptor_Curve adaptor(c);
        Standard_Real firstParam = c->FirstParameter();
        Standard_Real lastParam  = c->LastParameter();

        // For unbounded curves (e.g. Geom_Line where FirstParameter = -inf),
        // the IFC convention is that arc length is measured from the curve's
        // defining origin, which corresponds to parameter 0.
        Standard_Real startParam = firstParam;
        if (Precision::IsNegativeInfinite(firstParam) ||
            Precision::IsPositiveInfinite(lastParam))
        {
            startParam = 0.0;
        }

        if (std::abs(arcLength) < tolerance)
        {
            *outParameter = startParam;
            return XBIM_OK;
        }

        GCPnts_AbscissaPoint abscissa(adaptor, arcLength, startParam, tolerance);
        if (!abscissa.IsDone())
        {
            xbim_set_error("xbim_curve_parameter_at_length: "
                           "GCPnts_AbscissaPoint failed to converge");
            return XBIM_ERROR;
        }

        *outParameter = abscissa.Parameter();
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_set_error(e.GetMessageString() ? e.GetMessageString()
                       : "xbim_curve_parameter_at_length: OCCT exception");
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


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_project_point_3d(
    XbimContextHandle ctx,
    XbimCurveHandle   curveHandle,
    double px, double py, double pz,
    double tolerance,
    double* outParam)
{
    xbim_clear_error();

    if (!outParam)
    {
        xbim_set_error("xbim_curve_project_point_3d: outParam is NULL");
        return XBIM_INVALID_ARG;
    }
    *outParam = 0.0;

    if (!curveHandle || curveHandle->curve.IsNull())
    {
        xbim_set_error("xbim_curve_project_point_3d: curveHandle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        gp_Pnt pnt(px, py, pz);
        double param = 0.0;

        if (!GeomLib_Tool::Parameter(curveHandle->curve, pnt, tolerance, param))
        {
            xbim_set_error("xbim_curve_project_point_3d: point projection found no solution");
            return XBIM_ERROR;
        }

        *outParam = param;
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_project_point_3d");
        xbim_set_error("xbim_curve_project_point_3d: OCCT exception");
        return XBIM_ERROR;
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

#pragma region Segmented Reference Curve

XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_segmented_reference(
    XbimContextHandle   ctx,
    XbimCurveHandle     gradientCurveHandle,
    XbimCurve2dHandle*  segmentCurves,
    XbimLocationHandle* segmentLocations,
    int                 numSegments,
    XbimLocationHandle  endPointLocation,
    XbimCurveHandle*    outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_segmented_reference: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!gradientCurveHandle || gradientCurveHandle->curve.IsNull())
    {
        xbim_set_error("xbim_curve_build_segmented_reference: gradientCurveHandle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    Handle(Geom_GradientCurve) gradientCurve =
        Handle(Geom_GradientCurve)::DownCast(gradientCurveHandle->curve);
    if (gradientCurve.IsNull())
    {
        xbim_set_error("xbim_curve_build_segmented_reference: handle does not wrap a Geom_GradientCurve");
        return XBIM_INVALID_ARG;
    }

    try
    {
        std::vector<std::pair<Handle(Geom2d_Curve), TopLoc_Location>> superElevation;

        if (numSegments > 0 && segmentCurves && segmentLocations)
        {
            superElevation.reserve(numSegments);
            for (int i = 0; i < numSegments; ++i)
            {
                if (!segmentCurves[i] || segmentCurves[i]->curve.IsNull())
                {
                    xbim_set_error("xbim_curve_build_segmented_reference: null segment curve at index");
                    return XBIM_INVALID_ARG;
                }
                if (!segmentLocations[i])
                {
                    xbim_set_error("xbim_curve_build_segmented_reference: null segment location at index");
                    return XBIM_INVALID_ARG;
                }

                superElevation.emplace_back(
                    segmentCurves[i]->curve,
                    segmentLocations[i]->location);
            }
        }

        TopLoc_Location endLoc;
        bool hasEndPoint = false;
        if (endPointLocation)
        {
            endLoc = endPointLocation->location;
            hasEndPoint = true;
        }

        Handle(Geom_SegmentedReferenceCurve) segRef =
            new Geom_SegmentedReferenceCurve(
                gradientCurve, superElevation, endLoc, hasEndPoint);

        *outHandle = xbim_curve_create_from(segRef);
        if (!*outHandle)
        {
            xbim_set_error("xbim_curve_build_segmented_reference: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_segmented_reference");
        xbim_set_error("xbim_curve_build_segmented_reference: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_get_superelevation_and_tilt(
    XbimCurveHandle curveHandle,
    double          parameter,
    double*         outSuperElevation,
    double*         outCantTilt)
{
    xbim_clear_error();

    if (!curveHandle || curveHandle->curve.IsNull())
    {
        xbim_set_error("xbim_curve_get_superelevation_and_tilt: null handle");
        return XBIM_INVALID_HANDLE;
    }

    if (!outSuperElevation || !outCantTilt)
    {
        xbim_set_error("xbim_curve_get_superelevation_and_tilt: null output parameter");
        return XBIM_INVALID_ARG;
    }

    Handle(Geom_SegmentedReferenceCurve) segRef =
        Handle(Geom_SegmentedReferenceCurve)::DownCast(curveHandle->curve);
    if (segRef.IsNull())
    {
        xbim_set_error("xbim_curve_get_superelevation_and_tilt: handle does not wrap a Geom_SegmentedReferenceCurve");
        return XBIM_INVALID_ARG;
    }

    try
    {
        auto [superElev, cantTilt] = segRef->GetSuperelevationAndCantTiltAt(parameter);
        *outSuperElevation = superElev;
        *outCantTilt = cantTilt;
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(nullptr, e, "xbim_curve_get_superelevation_and_tilt");
        xbim_set_error("xbim_curve_get_superelevation_and_tilt: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion

#pragma region Composite Curve

/*
 * Approximate a 3D curve by sampling points and fitting a B-spline.
 * Used for curves that cannot be added directly to
 * GeomConvert_CompCurveToBSplineCurve.
 */
static Handle(Geom_BSplineCurve) ApproximateCurve3d(
    const Handle(Geom_Curve)& curve,
    Standard_Real first, Standard_Real last,
    int numPoints)
{
    if (numPoints < 2) numPoints = 200;

    TColgp_Array1OfPnt points(1, numPoints);
    double delta = (last - first) / (numPoints - 1);

    for (int i = 1; i <= numPoints; ++i)
    {
        double u = first + (i - 1) * delta;
        if (u > last) u = last;
        points.SetValue(i, curve->Value(u));
    }

    GeomAPI_PointsToBSpline fitter(points, 3, 8, GeomAbs_C2, 1.0e-6);
    if (!fitter.IsDone())
        return Handle(Geom_BSplineCurve)();

    return fitter.Curve();
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_reverse(XbimCurveHandle handle)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_curve_reverse: null handle");
        return XBIM_INVALID_HANDLE;
    }

    if (handle->curve.IsNull())
    {
        xbim_set_error("xbim_curve_reverse: curve is null");
        return XBIM_ERROR;
    }

    try
    {
        handle->curve->Reverse();
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_set_error(e.GetMessageString() ? e.GetMessageString()
                       : "xbim_curve_reverse: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_offset_3d(
    XbimContextHandle ctx,
    XbimCurveHandle   basisHandle,
    double            offset,
    double            refDirX, double refDirY, double refDirZ,
    XbimCurveHandle*  outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_offset_3d: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!basisHandle || basisHandle->curve.IsNull())
    {
        xbim_set_error("xbim_curve_build_offset_3d: basisHandle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        gp_Dir refDir(refDirX, refDirY, refDirZ);
        Handle(Geom_OffsetCurve) offsetCurve =
            new Geom_OffsetCurve(basisHandle->curve, offset, refDir);

        if (offsetCurve.IsNull())
        {
            xbim_set_error("xbim_curve_build_offset_3d: resulting offset curve is null");
            return XBIM_ERROR;
        }

        *outHandle = xbim_curve_create_from(offsetCurve);
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_offset_3d");
        xbim_set_error("xbim_curve_build_offset_3d: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_composite_bspline(
    XbimContextHandle   ctx,
    XbimCurveHandle*    curves,
    int                 numCurves,
    double              tolerance,
    XbimCurveHandle*    outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_curve_build_composite_bspline: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!curves || numCurves < 1)
    {
        xbim_set_error("xbim_curve_build_composite_bspline: need at least 1 curve");
        return XBIM_INVALID_ARG;
    }

    try
    {
        GeomConvert_CompCurveToBSplineCurve converter(Convert_RationalC1);

        gp_Pnt prevEnd;
        bool hasPrev = false;

        for (int i = 0; i < numCurves; ++i)
        {
            if (!curves[i] || curves[i]->curve.IsNull())
            {
                xbim_log_warning(ctx, "xbim_curve_build_composite_bspline: curve %d is NULL, skipping", i);
                continue;
            }

            Handle(Geom_Curve) c = curves[i]->curve;
            Handle(Geom_BoundedCurve) bounded = Handle(Geom_BoundedCurve)::DownCast(c);
            if (bounded.IsNull())
            {
                xbim_log_warning(ctx, "xbim_curve_build_composite_bspline: curve %d is not bounded, skipping", i);
                continue;
            }

            Standard_Real first = bounded->FirstParameter();
            Standard_Real last = bounded->LastParameter();

            /* Fill gap between segments with a line if needed */
            if (hasPrev)
            {
                gp_Pnt startPt = bounded->Value(first);
                if (!prevEnd.IsEqual(startPt, tolerance))
                {
                    gp_Dir gapDir(gp_Vec(prevEnd, startPt));
                    double gapLen = prevEnd.Distance(startPt);
                    Handle(Geom_TrimmedCurve) gapLine = new Geom_TrimmedCurve(
                        new Geom_Line(prevEnd, gapDir), 0.0, gapLen);
                    converter.Add(gapLine, tolerance, Standard_False);
                }
            }

            /* Try to add directly, approximate if needed */
            Handle(Geom_BSplineCurve) toAdd;

            if (!converter.Add(bounded, tolerance, Standard_False))
            {
                int n = std::max(200, static_cast<int>(std::abs(last - first) * 10) + 1);
                toAdd = ApproximateCurve3d(bounded, first, last, n);
            }

            if (!toAdd.IsNull())
            {
                if (!converter.Add(toAdd, tolerance, Standard_False))
                {
                    xbim_log_warning(ctx,
                        "xbim_curve_build_composite_bspline: failed to add curve %d after approximation", i);
                }
            }

            /* Track end point for gap filling */
            prevEnd = bounded->Value(last);
            hasPrev = true;
        }

        Handle(Geom_BSplineCurve) result = converter.BSplineCurve();
        if (result.IsNull())
        {
            xbim_set_error("xbim_curve_build_composite_bspline: composite B-spline is null");
            return XBIM_ERROR;
        }

        *outHandle = xbim_curve_create_from(result);
        return (*outHandle) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_curve_build_composite_bspline");
        xbim_set_error("xbim_curve_build_composite_bspline: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion
