/*
 * xbim_edge.cpp
 *
 * Implements edge construction and query functions via the flat C API.
 * Ports NEdgeFactory methods from the C++/CLI engine:
 *   - Build straight edge from two 3D points
 *   - Build edge from a curve handle with parameter bounds
 *   - Build circular arc edge from center, normal, radius, and angle range
 *   - Query edge length
 */

#include "xbim_edge.h"
#include "xbim_shape.h"
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
#include <BRepAdaptor_Curve.hxx>
#include <GCPnts_AbscissaPoint.hxx>
#include <Geom_Circle.hxx>
#include <Geom_TrimmedCurve.hxx>
#include <Standard_Failure.hxx>

/* ── xbim_edge_build_line ──────────────────────────────────────────────── */

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

/* ── xbim_edge_build_from_curve ────────────────────────────────────────── */

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

/* ── xbim_edge_build_circle_arc ────────────────────────────────────────── */

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

/* ── xbim_edge_length ──────────────────────────────────────────────────── */

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
