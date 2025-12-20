/*
 * xbim_edge.cpp
 *
 * Implements edge construction and query functions.
 */

#include "xbim_edge.h"
#include "xbim_shape.h"
#include "xbim_curve.h"
#include "xbim_curve2d.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <gp_Pnt.hxx>
#include <gp_Dir.hxx>
#include <gp_Ax2.hxx>
#include <gp_Circ.hxx>
#include <Precision.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Edge.hxx>
#include <BRep_Tool.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeEdge2d.hxx>
#include <BRepBuilderAPI_MakeVertex.hxx>
#include <BRepLib.hxx>
#include <BRepAdaptor_Curve.hxx>
#include <GCPnts_AbscissaPoint.hxx>
#include <Geom_Circle.hxx>
#include <Geom_TrimmedCurve.hxx>
#include <GC_MakeArcOfCircle.hxx>
#include <TopExp.hxx>
#include <TopoDS_Vertex.hxx>
#include <Geom_Line.hxx>
#include <Geom2d_Line.hxx>
#include <GeomAdaptor_Curve.hxx>
#include <Extrema_ExtPC.hxx>
#include <BRep_Builder.hxx>
#include <Standard_Failure.hxx>

#pragma region Helpers

/*
 * Locate a point on a curve, returning the parameter and distance.
 * Unwraps trimmed curves to work on the basis curve.
 * Checks endpoints first, then uses Extrema_ExtPC for interior points.
 */
static bool locate_vertex_on_curve(
    const Handle(Geom_Curve)& geomCurve,
    const gp_Pnt& P,
    double maxTolerance,
    double& parameter,
    double& actualDistance)
{
    /* Unwrap trimmed curves to get the basis curve */
    Handle(Geom_Curve) basisCurve = geomCurve;
    Handle(Geom_TrimmedCurve) trimmed = Handle(Geom_TrimmedCurve)::DownCast(basisCurve);
    while (!trimmed.IsNull())
    {
        basisCurve = trimmed->BasisCurve();
        trimmed = Handle(Geom_TrimmedCurve)::DownCast(basisCurve);
    }

    double Eps2 = maxTolerance * maxTolerance;
    GeomAdaptor_Curve GAC(basisCurve);

    gp_Pnt P1 = GAC.Value(GAC.FirstParameter());
    gp_Pnt P2 = GAC.Value(GAC.LastParameter());
    double D1 = P1.SquareDistance(P);
    double D2 = P2.SquareDistance(P);

    if ((D1 < D2) && (D1 <= Eps2))
    {
        parameter = GAC.FirstParameter();
        actualDistance = sqrt(D1);
        return true;
    }
    else if ((D2 < D1) && (D2 <= Eps2))
    {
        parameter = GAC.LastParameter();
        actualDistance = sqrt(D2);
        return true;
    }

    Extrema_ExtPC extrema(P, GAC);
    if (extrema.IsDone())
    {
        Standard_Integer index = 0, n = extrema.NbExt();
        double Dist2 = RealLast();

        for (Standard_Integer i = 1; i <= n; i++)
        {
            double dist2min = extrema.SquareDistance(i);
            if (dist2min < Dist2)
            {
                index = i;
                Dist2 = dist2min;
            }
        }

        if (index != 0 && Dist2 <= Eps2)
        {
            parameter = extrema.Point(index).Parameter();
            actualDistance = sqrt(Dist2);
            return true;
        }
    }
    return false;
}

#pragma endregion

#pragma region Edge Construction

XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_build_line(
    XbimContextHandle ctx,
    double startX, double startY, double startZ,
    double endX,   double endY,   double endZ,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_edge_build_line: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    try
    {
        gp_Pnt p1(startX, startY, startZ);
        gp_Pnt p2(endX, endY, endZ);

        if (p1.Distance(p2) < Precision::Confusion())
        {
            xbim_set_error("xbim_edge_build_line: start and end points are identical");
            xbim_log_warning(ctx, "Cannot build line edge: degenerate segment (zero length)");
            return XBIM_INVALID_ARG;
        }

        BRepBuilderAPI_MakeEdge edgeMaker(p1, p2);
        if (!edgeMaker.IsDone())
        {
            xbim_set_error("xbim_edge_build_line: edge construction failed");
            xbim_log_warning(ctx, "BRepBuilderAPI_MakeEdge failed for line edge");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Edge edge = edgeMaker.Edge();
        if (edge.IsNull())
        {
            xbim_set_error("xbim_edge_build_line: resulting edge is null");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(edge);
        if (!*outHandle)
        {
            xbim_set_error("xbim_edge_build_line: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_edge_build_line");
        xbim_set_error("xbim_edge_build_line: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_build_from_curve(
    XbimContextHandle ctx,
    XbimShapeHandle   curveEdgeHandle,
    double            param1,
    double            param2,
    XbimShapeHandle*  outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_edge_build_from_curve: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!curveEdgeHandle)
    {
        xbim_set_error("xbim_edge_build_from_curve: curveEdgeHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = curveEdgeHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_EDGE)
        {
            xbim_set_error("xbim_edge_build_from_curve: handle is not an edge");
            return XBIM_INVALID_ARG;
        }

        const TopoDS_Edge& sourceEdge = TopoDS::Edge(shape);

        /* Extract the 3D curve from the source edge */
        double first, last;
        Handle(Geom_Curve) curve = BRep_Tool::Curve(sourceEdge, first, last);
        if (curve.IsNull())
        {
            xbim_set_error("xbim_edge_build_from_curve: source edge has no 3D curve");
            return XBIM_NULL_SHAPE;
        }

        /* Use provided parameters, clamping to the curve's valid range */
        double u1 = param1;
        double u2 = param2;
        if (u1 < first) u1 = first;
        if (u2 > last) u2 = last;

        if (std::abs(u2 - u1) < Precision::PConfusion())
        {
            xbim_set_error("xbim_edge_build_from_curve: parameter range is degenerate");
            return XBIM_INVALID_ARG;
        }

        BRepBuilderAPI_MakeEdge edgeMaker(curve, u1, u2);
        if (!edgeMaker.IsDone())
        {
            xbim_set_error("xbim_edge_build_from_curve: edge construction failed");
            xbim_log_warning(ctx, "BRepBuilderAPI_MakeEdge failed for curve sub-edge");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Edge edge = edgeMaker.Edge();
        if (edge.IsNull())
        {
            xbim_set_error("xbim_edge_build_from_curve: resulting edge is null");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(edge);
        if (!*outHandle)
        {
            xbim_set_error("xbim_edge_build_from_curve: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_edge_build_from_curve");
        xbim_set_error("xbim_edge_build_from_curve: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_build_circle_arc(
    XbimContextHandle ctx,
    double centerX, double centerY, double centerZ,
    double normalX, double normalY, double normalZ,
    double radius,
    double startAngle,
    double endAngle,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_edge_build_circle_arc: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (radius <= Precision::Confusion())
    {
        xbim_set_error("xbim_edge_build_circle_arc: radius must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        gp_Pnt center(centerX, centerY, centerZ);
        gp_Dir normal(normalX, normalY, normalZ);
        gp_Ax2 ax2(center, normal);

        gp_Circ circle(ax2, radius);

        BRepBuilderAPI_MakeEdge edgeMaker(circle, startAngle, endAngle);
        if (!edgeMaker.IsDone())
        {
            xbim_set_error("xbim_edge_build_circle_arc: edge construction failed");
            xbim_log_warning(ctx, "BRepBuilderAPI_MakeEdge failed for circular arc");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Edge edge = edgeMaker.Edge();
        if (edge.IsNull())
        {
            xbim_set_error("xbim_edge_build_circle_arc: resulting edge is null");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(edge);
        if (!*outHandle)
        {
            xbim_set_error("xbim_edge_build_circle_arc: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_edge_build_circle_arc");
        xbim_set_error("xbim_edge_build_circle_arc: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_build_from_curve_handle(
    XbimContextHandle ctx,
    XbimCurveHandle   curveHandle,
    double startX, double startY, double startZ,
    double endX,   double endY,   double endZ,
    int              sameSense,
    double           tolerance,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_edge_build_from_curve_handle: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!curveHandle)
    {
        xbim_set_error("xbim_edge_build_from_curve_handle: curveHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        Handle(Geom_Curve) curve = curveHandle->curve;
        if (curve.IsNull())
        {
            xbim_set_error("xbim_edge_build_from_curve_handle: curve is null");
            return XBIM_NULL_SHAPE;
        }

        if (!sameSense)
        {
            curve = Handle(Geom_Curve)::DownCast(curve->Copy());
            curve->Reverse();
        }

        gp_Pnt pStart(startX, startY, startZ);
        gp_Pnt pEnd(endX, endY, endZ);

        TopoDS_Edge edge;

        /* If start and end are coincident, build a closed edge (seam) */
        if (pStart.Distance(pEnd) < tolerance)
        {
            BRepBuilderAPI_MakeEdge edgeMaker(curve);
            if (!edgeMaker.IsDone())
            {
                xbim_set_error("xbim_edge_build_from_curve_handle: closed edge construction failed");
                xbim_log_warning(ctx, "BRepBuilderAPI_MakeEdge failed for closed curve-handle edge");
                return XBIM_NULL_SHAPE;
            }
            edge = edgeMaker.Edge();
        }
        else
        {
            /* Build vertices from points */
            BRepBuilderAPI_MakeVertex mv1(pStart);
            BRepBuilderAPI_MakeVertex mv2(pEnd);
            TopoDS_Vertex startVertex = mv1.Vertex();
            TopoDS_Vertex endVertex = mv2.Vertex();

            /* Project vertices onto the curve to get exact parameters */
            double paramStart, paramEnd;
            double distStart, distEnd;

            if (!locate_vertex_on_curve(curve, pStart, tolerance, paramStart, distStart))
            {
                xbim_set_error("xbim_edge_build_from_curve_handle: start vertex not on curve within tolerance");
                xbim_log_warning(ctx, "Start vertex is not located on the curve within the required tolerance");
                return XBIM_INVALID_ARG;
            }
            if (!locate_vertex_on_curve(curve, pEnd, tolerance, paramEnd, distEnd))
            {
                xbim_set_error("xbim_edge_build_from_curve_handle: end vertex not on curve within tolerance");
                xbim_log_warning(ctx, "End vertex is not located on the curve within the required tolerance");
                return XBIM_INVALID_ARG;
            }

            /* Widen vertex tolerances if the point-to-curve distance exceeds them */
            BRep_Builder builder;
            double startVertexTol = BRep_Tool::Tolerance(startVertex);
            double endVertexTol = BRep_Tool::Tolerance(endVertex);
            if (distStart > startVertexTol)
                builder.UpdateVertex(startVertex, distStart);
            if (distEnd > endVertexTol)
                builder.UpdateVertex(endVertex, distEnd);

            BRepBuilderAPI_MakeEdge edgeMaker(curve, startVertex, endVertex, paramStart, paramEnd);
            if (!edgeMaker.IsDone())
            {
                xbim_set_error("xbim_edge_build_from_curve_handle: edge construction failed");
                xbim_log_warning(ctx, "BRepBuilderAPI_MakeEdge failed for curve-handle edge");
                return XBIM_NULL_SHAPE;
            }
            edge = edgeMaker.Edge();

            if (!BRepLib::BuildCurve3d(edge))
            {
                xbim_set_error("xbim_edge_build_from_curve_handle: failed to build 3D curve for edge");
                xbim_log_warning(ctx, "BRepLib::BuildCurve3d failed");
                return XBIM_ERROR;
            }
        }

        if (edge.IsNull())
        {
            xbim_set_error("xbim_edge_build_from_curve_handle: resulting edge is null");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(edge);
        if (!*outHandle)
        {
            xbim_set_error("xbim_edge_build_from_curve_handle: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_edge_build_from_curve_handle");
        xbim_set_error("xbim_edge_build_from_curve_handle: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_build_from_curve2d_handle(
    XbimContextHandle  ctx,
    XbimCurve2dHandle  curve2dHandle,
    double             startX, double startY,
    double             endX,   double endY,
    int                sameSense,
    double             tolerance,
    XbimShapeHandle*   outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_edge_build_from_curve2d_handle: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!curve2dHandle)
    {
        xbim_set_error("xbim_edge_build_from_curve2d_handle: curve2dHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        Handle(Geom2d_Curve) curve = curve2dHandle->curve;
        if (curve.IsNull())
        {
            xbim_set_error("xbim_edge_build_from_curve2d_handle: curve is null");
            return XBIM_NULL_SHAPE;
        }

        if (!sameSense)
        {
            curve = Handle(Geom2d_Curve)::DownCast(curve->Copy());
            curve->Reverse();
        }

        gp_Pnt2d pStart(startX, startY);
        gp_Pnt2d pEnd(endX, endY);

        TopoDS_Edge edge;
        bool isClosed = pStart.Distance(pEnd) < tolerance;

        if (isClosed)
        {
            BRepBuilderAPI_MakeEdge2d edgeMaker(curve);
            if (!edgeMaker.IsDone())
            {
                xbim_set_error("xbim_edge_build_from_curve2d_handle: edge construction failed (closed)");
                xbim_log_warning(ctx, "BRepBuilderAPI_MakeEdge2d failed for closed curve2d edge");
                return XBIM_NULL_SHAPE;
            }
            edge = edgeMaker.Edge();
        }
        else
        {
            BRepBuilderAPI_MakeEdge2d edgeMaker(curve, pStart, pEnd);
            if (!edgeMaker.IsDone())
            {
                xbim_set_error("xbim_edge_build_from_curve2d_handle: edge construction failed (open)");
                xbim_log_warning(ctx, "BRepBuilderAPI_MakeEdge2d failed for curve2d-handle edge");
                return XBIM_NULL_SHAPE;
            }
            edge = edgeMaker.Edge();
        }

        if (edge.IsNull())
        {
            xbim_set_error("xbim_edge_build_from_curve2d_handle: resulting edge is null");
            return XBIM_NULL_SHAPE;
        }

        /* Build the 3D curve representation required by downstream BRep operations */
        if (!BRepLib::BuildCurve3d(edge))
        {
            xbim_set_error("xbim_edge_build_from_curve2d_handle: failed to build 3D curve for 2D edge");
            xbim_log_warning(ctx, "BRepLib::BuildCurve3d failed for 2D edge");
            return XBIM_ERROR;
        }

        *outHandle = xbim_shape_create_from(edge);
        if (!*outHandle)
        {
            xbim_set_error("xbim_edge_build_from_curve2d_handle: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_edge_build_from_curve2d_handle");
        xbim_set_error("xbim_edge_build_from_curve2d_handle: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_from_curve_handle(
    XbimContextHandle ctx,
    XbimCurveHandle   curveHandle,
    XbimShapeHandle*  outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_edge_from_curve_handle: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!curveHandle)
    {
        xbim_set_error("xbim_edge_from_curve_handle: curveHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        Handle(Geom_Curve) curve = curveHandle->curve;
        if (curve.IsNull())
        {
            xbim_set_error("xbim_edge_from_curve_handle: curve is null");
            return XBIM_NULL_SHAPE;
        }

        /* Unbounded lines cannot be passed directly to MakeEdge;
           use the parametric range [FirstParameter, LastParameter] which
           for trimmed curves gives the correct bounds. For truly infinite
           lines this will fail — callers should trim first. */
        Handle(Geom_Line) line = Handle(Geom_Line)::DownCast(curve);
        if (!line.IsNull())
        {
            xbim_set_error("xbim_edge_from_curve_handle: unbounded Geom_Line cannot be converted to an edge; trim it first");
            return XBIM_INVALID_ARG;
        }

        BRepBuilderAPI_MakeEdge edgeMaker(curve);
        if (!edgeMaker.IsDone())
        {
            xbim_set_error("xbim_edge_from_curve_handle: edge construction failed");
            xbim_log_warning(ctx, "BRepBuilderAPI_MakeEdge failed for curve handle");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Edge edge = edgeMaker.Edge();
        if (edge.IsNull())
        {
            xbim_set_error("xbim_edge_from_curve_handle: resulting edge is null");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(edge);
        if (!*outHandle)
        {
            xbim_set_error("xbim_edge_from_curve_handle: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_edge_from_curve_handle");
        xbim_set_error("xbim_edge_from_curve_handle: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_from_curve2d_handle(
    XbimContextHandle  ctx,
    XbimCurve2dHandle  curve2dHandle,
    XbimShapeHandle*   outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_edge_from_curve2d_handle: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!curve2dHandle)
    {
        xbim_set_error("xbim_edge_from_curve2d_handle: curve2dHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        Handle(Geom2d_Curve) curve = curve2dHandle->curve;
        if (curve.IsNull())
        {
            xbim_set_error("xbim_edge_from_curve2d_handle: curve is null");
            return XBIM_NULL_SHAPE;
        }

        Handle(Geom2d_Line) line = Handle(Geom2d_Line)::DownCast(curve);
        if (!line.IsNull())
        {
            xbim_set_error("xbim_edge_from_curve2d_handle: unbounded Geom2d_Line cannot be converted to an edge; trim it first");
            return XBIM_INVALID_ARG;
        }

        BRepBuilderAPI_MakeEdge2d edgeMaker(curve);
        if (!edgeMaker.IsDone())
        {
            xbim_set_error("xbim_edge_from_curve2d_handle: edge construction failed");
            xbim_log_warning(ctx, "BRepBuilderAPI_MakeEdge2d failed for curve2d handle");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Edge edge = edgeMaker.Edge();
        if (edge.IsNull())
        {
            xbim_set_error("xbim_edge_from_curve2d_handle: resulting edge is null");
            return XBIM_NULL_SHAPE;
        }

        if (!BRepLib::BuildCurve3d(edge))
        {
            xbim_set_error("xbim_edge_from_curve2d_handle: failed to build 3D curve for 2D edge");
            xbim_log_warning(ctx, "BRepLib::BuildCurve3d failed for 2D edge");
            return XBIM_ERROR;
        }

        *outHandle = xbim_shape_create_from(edge);
        if (!*outHandle)
        {
            xbim_set_error("xbim_edge_from_curve2d_handle: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_edge_from_curve2d_handle");
        xbim_set_error("xbim_edge_from_curve2d_handle: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_build_circle_arc_3pt(
    XbimContextHandle ctx,
    double p1X, double p1Y, double p1Z,
    double p2X, double p2Y, double p2Z,
    double p3X, double p3Y, double p3Z,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_edge_build_circle_arc_3pt: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    try
    {
        gp_Pnt start(p1X, p1Y, p1Z);
        gp_Pnt mid(p2X, p2Y, p2Z);
        gp_Pnt end(p3X, p3Y, p3Z);

        if (start.Distance(mid) < Precision::Confusion() ||
            mid.Distance(end) < Precision::Confusion() ||
            start.Distance(end) < Precision::Confusion())
        {
            xbim_set_error("xbim_edge_build_circle_arc_3pt: two or more points are coincident");
            return XBIM_INVALID_ARG;
        }

        /* Check collinearity: if the three points are collinear, build a line edge instead */
        gp_Vec v1(start, mid);
        gp_Vec v2(start, end);
        if (v1.CrossMagnitude(v2) < Precision::Confusion() * v1.Magnitude())
        {
            /* Collinear points - fall back to a straight line edge */
            BRepBuilderAPI_MakeEdge edgeMaker(start, end);
            if (!edgeMaker.IsDone())
            {
                xbim_set_error("xbim_edge_build_circle_arc_3pt: line edge fallback failed");
                return XBIM_NULL_SHAPE;
            }
            *outHandle = xbim_shape_create_from(edgeMaker.Edge());
            return *outHandle ? XBIM_OK : XBIM_ERROR;
        }

        GC_MakeArcOfCircle arcMaker(start, mid, end);
        if (!arcMaker.IsDone())
        {
            xbim_set_error("xbim_edge_build_circle_arc_3pt: GC_MakeArcOfCircle failed");
            xbim_log_warning(ctx, "Cannot build circular arc through 3 points");
            return XBIM_NULL_SHAPE;
        }

        Handle(Geom_TrimmedCurve) arc = arcMaker.Value();
        BRepBuilderAPI_MakeEdge edgeMaker(arc);
        if (!edgeMaker.IsDone())
        {
            xbim_set_error("xbim_edge_build_circle_arc_3pt: edge construction failed");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Edge edge = edgeMaker.Edge();
        if (edge.IsNull())
        {
            xbim_set_error("xbim_edge_build_circle_arc_3pt: resulting edge is null");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(edge);
        if (!*outHandle)
        {
            xbim_set_error("xbim_edge_build_circle_arc_3pt: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_edge_build_circle_arc_3pt");
        xbim_set_error("xbim_edge_build_circle_arc_3pt: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion

#pragma region Edge Queries

XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_length(
    XbimShapeHandle edgeHandle,
    double*         outLength)
{
    xbim_clear_error();

    if (!outLength)
    {
        xbim_set_error("xbim_edge_length: outLength is NULL");
        return XBIM_INVALID_ARG;
    }
    *outLength = 0.0;

    if (!edgeHandle)
    {
        xbim_set_error("xbim_edge_length: edgeHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = edgeHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_EDGE)
        {
            xbim_set_error("xbim_edge_length: handle is not an edge");
            return XBIM_INVALID_ARG;
        }

        const TopoDS_Edge& edge = TopoDS::Edge(shape);
        BRepAdaptor_Curve adaptor(edge);
        *outLength = GCPnts_AbscissaPoint::Length(adaptor);

        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_edge_length: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_tolerance(
    XbimShapeHandle edgeHandle,
    double*         outTolerance)
{
    xbim_clear_error();
    if (!outTolerance) { xbim_set_error("xbim_edge_tolerance: outTolerance is NULL"); return XBIM_INVALID_ARG; }
    *outTolerance = 0.0;
    if (!edgeHandle) { xbim_set_error("xbim_edge_tolerance: edgeHandle is NULL"); return XBIM_INVALID_HANDLE; }

    try
    {
        const TopoDS_Shape& shape = edgeHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_EDGE)
        {
            xbim_set_error("xbim_edge_tolerance: handle is not an edge");
            return XBIM_INVALID_ARG;
        }

        *outTolerance = BRep_Tool::Tolerance(TopoDS::Edge(shape));
        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_edge_tolerance: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_get_curve(
    XbimShapeHandle  edgeHandle,
    XbimCurveHandle* outCurve,
    double*          outParam1,
    double*          outParam2)
{
    xbim_clear_error();
    if (!outCurve || !outParam1 || !outParam2)
    {
        xbim_set_error("xbim_edge_get_curve: output pointer is NULL");
        return XBIM_INVALID_ARG;
    }
    *outCurve = nullptr;
    *outParam1 = 0.0;
    *outParam2 = 0.0;

    if (!edgeHandle)
    {
        xbim_set_error("xbim_edge_get_curve: edgeHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = edgeHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_EDGE)
        {
            xbim_set_error("xbim_edge_get_curve: handle is not an edge");
            return XBIM_INVALID_ARG;
        }

        Standard_Real p1, p2;
        Handle(Geom_Curve) curve = BRep_Tool::Curve(TopoDS::Edge(shape), p1, p2);
        if (curve.IsNull())
        {
            xbim_set_error("xbim_edge_get_curve: edge has no 3D curve");
            return XBIM_NULL_SHAPE;
        }

        *outCurve = xbim_curve_create_from(curve);
        if (!*outCurve)
        {
            xbim_set_error("xbim_edge_get_curve: memory allocation failed");
            return XBIM_ERROR;
        }
        *outParam1 = p1;
        *outParam2 = p2;
        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_edge_get_curve: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_vertices(
    XbimShapeHandle edgeHandle,
    XbimShapeHandle* outStart,
    XbimShapeHandle* outEnd)
{
    xbim_clear_error();
    if (!outStart || !outEnd)
    {
        xbim_set_error("xbim_edge_vertices: outStart or outEnd is NULL");
        return XBIM_INVALID_ARG;
    }
    *outStart = nullptr;
    *outEnd = nullptr;
    if (!edgeHandle) { xbim_set_error("xbim_edge_vertices: edgeHandle is NULL"); return XBIM_INVALID_HANDLE; }

    try
    {
        const TopoDS_Shape& shape = edgeHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_EDGE)
        {
            xbim_set_error("xbim_edge_vertices: handle is not an edge");
            return XBIM_INVALID_ARG;
        }

        TopoDS_Vertex vFirst, vLast;
        TopExp::Vertices(TopoDS::Edge(shape), vFirst, vLast);

        if (!vFirst.IsNull())
            *outStart = xbim_shape_create_from(vFirst);
        if (!vLast.IsNull())
            *outEnd = xbim_shape_create_from(vLast);

        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_edge_vertices: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion
