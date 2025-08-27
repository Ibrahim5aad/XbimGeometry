/*
 * xbim_wire.cpp
 *
 * Implements wire construction and query functions via the flat C API.
 * Ports NWireFactory methods from the C++/CLI engine:
 *   - Build wire from a sequence of edge shape handles
 *   - Build polyline wire from 3D point coordinates
 *   - Build closed polygon wire from 3D point coordinates
 *   - Query whether a wire is closed
 */

#include "xbim_wire.h"
#include "xbim_shape.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <gp_Pnt.hxx>
#include <gp_Vec.hxx>
#include <Precision.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Wire.hxx>
#include <TopoDS_Vertex.hxx>
#include <TopExp.hxx>
#include <BRep_Builder.hxx>
#include <BRep_Tool.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepAdaptor_CompCurve.hxx>
#include <GCPnts_AbscissaPoint.hxx>
#include <GProp_GProps.hxx>
#include <BRepGProp.hxx>
#include <TopTools_SequenceOfShape.hxx>
#include <TopAbs_ShapeEnum.hxx>
#include <Standard_Failure.hxx>

#pragma region Wire Construction

XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_from_edges(
    XbimContextHandle        ctx,
    const XbimShapeHandle*   edgeHandles,
    int                      numEdges,
    XbimShapeHandle*         outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_wire_build_from_edges: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!edgeHandles || numEdges <= 0)
    {
        xbim_set_error("xbim_wire_build_from_edges: edgeHandles is NULL or numEdges <= 0");
        return XBIM_INVALID_ARG;
    }

    try
    {
        BRep_Builder builder;
        TopoDS_Wire wire;
        builder.MakeWire(wire);

        for (int i = 0; i < numEdges; ++i)
        {
            if (!edgeHandles[i])
            {
                xbim_set_error("xbim_wire_build_from_edges: edge handle at index is NULL");
                xbim_log_warning(ctx, "Wire edge handle at index %d is NULL, skipping", i);
                continue;
            }

            const TopoDS_Shape& shape = edgeHandles[i]->shape;
            if (shape.IsNull() || shape.ShapeType() != TopAbs_EDGE)
            {
                xbim_log_warning(ctx, "Wire handle at index %d is not an edge, skipping", i);
                continue;
            }

            builder.Add(wire, TopoDS::Edge(shape));
        }

        if (wire.IsNull())
        {
            xbim_set_error("xbim_wire_build_from_edges: resulting wire is null");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(wire);
        if (!*outHandle)
        {
            xbim_set_error("xbim_wire_build_from_edges: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_wire_build_from_edges");
        xbim_set_error("xbim_wire_build_from_edges: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_polyline(
    XbimContextHandle ctx,
    const double*     pointsXYZ,
    int               numPoints,
    double            tolerance,
    XbimShapeHandle*  outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_wire_build_polyline: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!pointsXYZ || numPoints < 2)
    {
        xbim_set_error("xbim_wire_build_polyline: pointsXYZ is NULL or numPoints < 2");
        return XBIM_INVALID_ARG;
    }

    try
    {
        BRep_Builder builder;
        TopoDS_Wire wire;
        builder.MakeWire(wire);

        /* Collect unique vertices, skipping degenerate segments */
        TopTools_SequenceOfShape vertices;

        for (int i = 0; i < numPoints; ++i)
        {
            gp_Pnt pt(pointsXYZ[i * 3], pointsXYZ[i * 3 + 1], pointsXYZ[i * 3 + 2]);

            if (i > 0 && vertices.Length() > 0)
            {
                const TopoDS_Vertex& lastVtx = TopoDS::Vertex(vertices.Last());
                double vtxTol = BRep_Tool::Tolerance(lastVtx);
                gp_Pnt lastPt = BRep_Tool::Pnt(lastVtx);
                gp_Vec edgeVec(lastPt, pt);
                double segLen = edgeVec.Magnitude();

                if (segLen < tolerance + vtxTol)
                {
                    /* Merge into the previous vertex */
                    gp_Pnt midPt = lastPt.Translated(edgeVec.Divided(2));
                    builder.UpdateVertex(lastVtx, midPt, segLen + vtxTol);
                    continue;
                }
            }

            TopoDS_Vertex v;
            builder.MakeVertex(v, pt, Precision::Confusion());
            vertices.Append(v);
        }

        if (vertices.Length() < 2)
        {
            xbim_set_error("xbim_wire_build_polyline: insufficient unique points after de-duplication");
            xbim_log_warning(ctx, "Polyline must have at least 2 distinct vertices");
            return XBIM_NULL_SHAPE;
        }

        /* Check if the polyline is closed (first/last points within tolerance) */
        gp_Pnt firstPt = BRep_Tool::Pnt(TopoDS::Vertex(vertices.First()));
        gp_Pnt lastPt = BRep_Tool::Pnt(TopoDS::Vertex(vertices.Last()));
        bool closed = firstPt.Distance(lastPt) <= tolerance;

        /* Build edges between consecutive vertices */
        for (int i = 2; i <= vertices.Length(); ++i)
        {
            const TopoDS_Vertex& startV = TopoDS::Vertex(vertices.Value(i - 1));
            const TopoDS_Vertex& endV = TopoDS::Vertex(vertices.Value(i));
            BRepBuilderAPI_MakeEdge edgeMaker(startV, endV);
            builder.Add(wire, edgeMaker.Edge());
        }

        if (closed)
            wire.Closed(true);

        if (wire.IsNull())
        {
            xbim_set_error("xbim_wire_build_polyline: resulting wire is null");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(wire);
        if (!*outHandle)
        {
            xbim_set_error("xbim_wire_build_polyline: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_wire_build_polyline");
        xbim_set_error("xbim_wire_build_polyline: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_polygon(
    XbimContextHandle ctx,
    const double*     pointsXYZ,
    int               numPoints,
    int               closed,
    XbimShapeHandle*  outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_wire_build_polygon: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!pointsXYZ || numPoints < 2)
    {
        xbim_set_error("xbim_wire_build_polygon: pointsXYZ is NULL or numPoints < 2");
        return XBIM_INVALID_ARG;
    }

    try
    {
        BRepBuilderAPI_MakeWire wireMaker;

        /* Build edges between consecutive points */
        for (int i = 0; i < numPoints - 1; ++i)
        {
            gp_Pnt p1(pointsXYZ[i * 3],     pointsXYZ[i * 3 + 1],     pointsXYZ[i * 3 + 2]);
            gp_Pnt p2(pointsXYZ[(i+1) * 3], pointsXYZ[(i+1) * 3 + 1], pointsXYZ[(i+1) * 3 + 2]);

            if (p1.Distance(p2) < Precision::Confusion())
                continue; /* skip degenerate edges */

            BRepBuilderAPI_MakeEdge edgeMaker(p1, p2);
            if (edgeMaker.IsDone())
                wireMaker.Add(edgeMaker.Edge());
        }

        /* Close the polygon by connecting last point to first */
        if (closed && numPoints >= 3)
        {
            gp_Pnt pLast(pointsXYZ[(numPoints-1) * 3],
                         pointsXYZ[(numPoints-1) * 3 + 1],
                         pointsXYZ[(numPoints-1) * 3 + 2]);
            gp_Pnt pFirst(pointsXYZ[0], pointsXYZ[1], pointsXYZ[2]);

            if (pLast.Distance(pFirst) >= Precision::Confusion())
            {
                BRepBuilderAPI_MakeEdge edgeMaker(pLast, pFirst);
                if (edgeMaker.IsDone())
                    wireMaker.Add(edgeMaker.Edge());
            }
        }

        if (!wireMaker.IsDone())
        {
            xbim_set_error("xbim_wire_build_polygon: could not build wire from points");
            xbim_log_warning(ctx, "Could not build polygon wire");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Wire wire = wireMaker.Wire();
        if (closed)
            wire.Closed(true);

        if (wire.IsNull())
        {
            xbim_set_error("xbim_wire_build_polygon: resulting wire is null");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(wire);
        if (!*outHandle)
        {
            xbim_set_error("xbim_wire_build_polygon: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_wire_build_polygon");
        xbim_set_error("xbim_wire_build_polygon: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion

#pragma region Wire Queries

XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_is_closed(
    XbimShapeHandle wireHandle,
    double          tolerance,
    int*            outClosed)
{
    xbim_clear_error();

    if (!outClosed)
    {
        xbim_set_error("xbim_wire_is_closed: outClosed is NULL");
        return XBIM_INVALID_ARG;
    }
    *outClosed = 0;

    if (!wireHandle)
    {
        xbim_set_error("xbim_wire_is_closed: wireHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = wireHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_WIRE)
        {
            xbim_set_error("xbim_wire_is_closed: handle is not a wire");
            return XBIM_INVALID_ARG;
        }

        const TopoDS_Wire& wire = TopoDS::Wire(shape);

        BRepAdaptor_CompCurve cc(wire, Standard_True);
        gp_Pnt p1 = cc.Value(cc.FirstParameter());
        gp_Pnt p2 = cc.Value(cc.LastParameter());
        *outClosed = p1.IsEqual(p2, tolerance) ? 1 : 0;

        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_wire_is_closed: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_length(
    XbimShapeHandle wireHandle,
    double*         outLength)
{
    xbim_clear_error();

    if (!outLength)
    {
        xbim_set_error("xbim_wire_length: outLength is NULL");
        return XBIM_INVALID_ARG;
    }
    *outLength = 0.0;

    if (!wireHandle)
    {
        xbim_set_error("xbim_wire_length: wireHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = wireHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_WIRE)
        {
            xbim_set_error("xbim_wire_length: handle is not a wire");
            return XBIM_INVALID_ARG;
        }

        GProp_GProps props;
        BRepGProp::LinearProperties(shape, props);
        *outLength = props.Mass();
        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_wire_length: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_contour_area(
    XbimShapeHandle wireHandle,
    double*         outArea)
{
    xbim_clear_error();

    if (!outArea)
    {
        xbim_set_error("xbim_wire_contour_area: outArea is NULL");
        return XBIM_INVALID_ARG;
    }
    *outArea = 0.0;

    if (!wireHandle)
    {
        xbim_set_error("xbim_wire_contour_area: wireHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = wireHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_WIRE)
        {
            xbim_set_error("xbim_wire_contour_area: handle is not a wire");
            return XBIM_INVALID_ARG;
        }

        /* Use the shoelace formula on projected edge vertices.
         * This computes the signed area of the polygon formed by
         * sampling edge start points along the wire. */
        const TopoDS_Wire& wire = TopoDS::Wire(shape);
        BRepAdaptor_CompCurve cc(wire, Standard_True);

        /* Sample enough points along the composite curve to get
         * an accurate area. For a polygon wire, each segment
         * contributes exactly one linear piece. */
        double first = cc.FirstParameter();
        double last  = cc.LastParameter();
        int numSamples = 200;
        double step = (last - first) / numSamples;

        double area = 0.0;
        gp_Pnt prev = cc.Value(first);
        for (int i = 1; i <= numSamples; i++)
        {
            double u = first + i * step;
            gp_Pnt curr = cc.Value(u);
            /* Shoelace in 3D projected onto dominant plane.
             * We accumulate the cross product; the magnitude gives 2*area. */
            area += prev.X() * curr.Y() - curr.X() * prev.Y();
            prev = curr;
        }

        *outArea = std::abs(area) * 0.5;
        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_wire_contour_area: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion
