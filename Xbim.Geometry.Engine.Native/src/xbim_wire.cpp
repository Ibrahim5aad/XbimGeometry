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
#include "xbim_curve2d.h"
#include "xbim_logging.h"

#include <gp_Pnt.hxx>
#include <gp_Vec.hxx>
#include <gp_Lin.hxx>
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
#include <BRepAdaptor_Curve.hxx>
#include <BRepTools_WireExplorer.hxx>
#include <GCPnts_AbscissaPoint.hxx>
#include <GProp_GProps.hxx>
#include <BRepGProp.hxx>
#include <TopTools_SequenceOfShape.hxx>
#include <TopAbs_ShapeEnum.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <BRepBuilderAPI_MakeEdge2d.hxx>
#include <BRepLib.hxx>
#include <ShapeFix_Edge.hxx>
#include <Standard_Failure.hxx>
#include <ShapeAnalysis.hxx>
#include <Geom_TrimmedCurve.hxx>
#include <Geom_Line.hxx>
#include <GeomLib_Tool.hxx>
#include <GeomAbs_CurveType.hxx>


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
        BRepBuilderAPI_MakeWire wireMaker;

        for (int i = 0; i < numEdges; ++i)
        {
            if (!edgeHandles[i])
            {
                xbim_log_warning(ctx, "Wire edge handle at index %d is NULL, skipping", i);
                continue;
            }

            const TopoDS_Shape& shape = edgeHandles[i]->shape;
            if (shape.IsNull() || shape.ShapeType() != TopAbs_EDGE)
            {
                xbim_log_warning(ctx, "Wire handle at index %d is not an edge, skipping", i);
                continue;
            }

            wireMaker.Add(TopoDS::Edge(shape));
        }

        if (!wireMaker.IsDone())
        {
            xbim_set_error("xbim_wire_build_from_edges: could not build wire from edges");
            xbim_log_warning(ctx, "BRepBuilderAPI_MakeWire failed");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Wire wire = wireMaker.Wire();
        if (wire.IsNull())
        {
            xbim_set_error("xbim_wire_build_from_edges: resulting wire is null");
            return XBIM_NULL_SHAPE;
        }

        /* Detect closure: check if first and last vertices coincide */
        TopoDS_Vertex vFirst, vLast;
        TopExp::Vertices(wire, vFirst, vLast);
        if (!vFirst.IsNull() && !vLast.IsNull())
        {
            gp_Pnt p1 = BRep_Tool::Pnt(vFirst);
            gp_Pnt p2 = BRep_Tool::Pnt(vLast);
            if (p1.IsEqual(p2, Precision::Confusion()))
                wire.Closed(true);
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
        bool closed = (vertices.Length() > 2) && (firstPt.Distance(lastPt) <= tolerance);

        /* If closed, drop the duplicate last vertex — the closing edge
         * will reuse the first vertex to produce proper shared topology. */
        if (closed)
            vertices.Remove(vertices.Length());

        /* Build edges between consecutive vertices */
        int nv = vertices.Length();
        for (int i = 2; i <= nv; ++i)
        {
            const TopoDS_Vertex& startV = TopoDS::Vertex(vertices.Value(i - 1));
            const TopoDS_Vertex& endV = TopoDS::Vertex(vertices.Value(i));
            BRepBuilderAPI_MakeEdge edgeMaker(startV, endV);
            builder.Add(wire, edgeMaker.Edge());
        }

        /* Add closing edge sharing the first vertex */
        if (closed)
        {
            BRepBuilderAPI_MakeEdge closingEdge(
                TopoDS::Vertex(vertices.Value(nv)),
                TopoDS::Vertex(vertices.First()));
            builder.Add(wire, closingEdge.Edge());
            wire.Closed(true);
        }

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


XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_from_2d_curves(
    XbimContextHandle   ctx,
    XbimCurve2dHandle*  curves,
    int                 numCurves,
    double              tolerance,
    double              gapSize,
    XbimShapeHandle*    outWire)
{
    xbim_clear_error();

    if (!outWire)
    {
        xbim_set_error("xbim_wire_build_from_2d_curves: outWire is NULL");
        return XBIM_INVALID_ARG;
    }
    *outWire = nullptr;

    if (!curves || numCurves <= 0)
    {
        xbim_set_error("xbim_wire_build_from_2d_curves: curves is NULL or numCurves <= 0");
        return XBIM_INVALID_ARG;
    }

    try
    {
        ShapeFix_Edge toleranceFixer;
        TopTools_SequenceOfShape edges;
        BRep_Builder builder;

        TopoDS_Edge theFirstEdge;
        TopoDS_Vertex theFirstVertex;
        gp_Pnt theFirstPoint;
        bool isClosed = false;

        for (int i = 0; i < numCurves; ++i)
        {
            if (!curves[i] || curves[i]->curve.IsNull())
            {
                xbim_log_warning(ctx, "xbim_wire_build_from_2d_curves: curve at index %d is NULL, skipping", i);
                continue;
            }

            const Handle(Geom2d_Curve)& segment = curves[i]->curve;
            bool tolerancesAdjusted = false;
            TopoDS_Edge anEdge;

            if (edges.Length() == 0) /* first edge */
            {
                BRepBuilderAPI_MakeEdge2d edgeMaker(segment);
                if (!edgeMaker.IsDone())
                {
                    xbim_log_warning(ctx, "xbim_wire_build_from_2d_curves: MakeEdge2d failed for first segment");
                    continue;
                }
                anEdge = edgeMaker.Edge();
                theFirstEdge = anEdge;
                theFirstVertex = TopExp::FirstVertex(anEdge);
                theFirstPoint = BRep_Tool::Pnt(theFirstVertex);
            }
            else /* subsequent edges — share vertices */
            {
                const TopoDS_Edge& lastEdge = TopoDS::Edge(edges.Last());
                gp_Pnt lastEdgeEndPoint = BRep_Tool::Pnt(TopExp::LastVertex(lastEdge));

                gp_Pnt2d segStart2d = segment->Value(segment->FirstParameter());
                gp_Pnt2d segEnd2d   = segment->Value(segment->LastParameter());
                gp_Pnt segStartPoint(segStart2d.X(), segStart2d.Y(), 0);
                gp_Pnt segEndPoint(segEnd2d.X(), segEnd2d.Y(), 0);

                double gap = segStartPoint.Distance(lastEdgeEndPoint);
                if (gap > gapSize)
                {
                    xbim_log_warning(ctx,
                        "xbim_wire_build_from_2d_curves: segment %d gap %.6f exceeds gapSize %.6f",
                        i, gap, gapSize);
                }

                /* Adjust vertex tolerance to bridge the gap */
                if (gap > Precision::Confusion())
                {
                    TopoDS_Vertex lastV = TopExp::LastVertex(lastEdge);
                    double newTol = std::max(BRep_Tool::Tolerance(lastV), gap / 2.0 + Precision::Confusion());
                    builder.UpdateVertex(lastV, newTol);
                    tolerancesAdjusted = true;
                }

                /* Check if this is the last segment and closes the wire */
                TopoDS_Vertex segEndVertex;
                if (i == numCurves - 1 && theFirstPoint.Distance(segEndPoint) < gapSize)
                {
                    isClosed = true;
                    double closingGap = segEndPoint.Distance(theFirstPoint);
                    if (closingGap > Precision::Confusion())
                    {
                        double newTol = std::max(BRep_Tool::Tolerance(theFirstVertex),
                                                 closingGap / 2.0 + Precision::Confusion());
                        builder.UpdateVertex(theFirstVertex, newTol);
                        tolerancesAdjusted = true;
                    }
                }
                else
                {
                    builder.MakeVertex(segEndVertex, segEndPoint, tolerance);
                }

                BRepBuilderAPI_MakeEdge2d edgeMaker(
                    segment,
                    TopExp::LastVertex(lastEdge),
                    isClosed ? theFirstVertex : segEndVertex);

                if (!edgeMaker.IsDone())
                {
                    xbim_log_warning(ctx,
                        "xbim_wire_build_from_2d_curves: MakeEdge2d failed for segment %d (error %d)",
                        i, (int)edgeMaker.Error());
                    continue;
                }

                anEdge = edgeMaker.Edge();

                if (tolerancesAdjusted)
                {
                    if (isClosed)
                        toleranceFixer.FixVertexTolerance(theFirstEdge);
                    else
                        toleranceFixer.FixVertexTolerance(lastEdge);
                    toleranceFixer.FixVertexTolerance(anEdge);
                }
            }

            /* Generate 3D curve from the 2D edge */
            bool ok = BRepLib::BuildCurve3d(anEdge, tolerance);
            if (ok)
                edges.Append(anEdge);
            else
                xbim_log_warning(ctx,
                    "xbim_wire_build_from_2d_curves: BuildCurve3d failed for segment %d", i);
        }

        if (edges.Length() == 0)
        {
            xbim_set_error("xbim_wire_build_from_2d_curves: no valid edges built");
            return XBIM_NULL_SHAPE;
        }

        /* Assemble wire */
        TopoDS_Wire wire;
        builder.MakeWire(wire);
        for (auto it = edges.cbegin(); it != edges.cend(); ++it)
        {
            builder.Add(wire, *it);
        }
        if (isClosed)
            wire.Closed(true);

        *outWire = xbim_shape_create_from(wire);
        if (!*outWire)
        {
            xbim_set_error("xbim_wire_build_from_2d_curves: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_wire_build_from_2d_curves");
        xbim_set_error("xbim_wire_build_from_2d_curves: OCCT exception");
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
        *outClosed = p1.IsEqual(p2, tolerance) ? XBIM_TRUE : XBIM_FALSE;

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
        const TopoDS_Wire& wire = TopoDS::Wire(shape);
        *outArea = ShapeAnalysis::ContourArea(wire);
        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_wire_contour_area: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion

#pragma region Wire Trimming

/*
 * Project a 3D point onto a wire's composite curve and return the
 * parametric position along the wire.  The parameter is expressed in
 * accumulated arc-length space (edge-by-edge) so that it can be used
 * directly with xbim_wire_build_trimmed.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_get_parameter(
    XbimShapeHandle wireHandle,
    double          pointX,
    double          pointY,
    double          pointZ,
    double          tolerance,
    double*         outParam)
{
    xbim_clear_error();

    if (!outParam)
    {
        xbim_set_error("xbim_wire_get_parameter: outParam is NULL");
        return XBIM_INVALID_ARG;
    }
    *outParam = 0.0;

    if (!wireHandle)
    {
        xbim_set_error("xbim_wire_get_parameter: wireHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = wireHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_WIRE)
        {
            xbim_set_error("xbim_wire_get_parameter: handle is not a wire");
            return XBIM_INVALID_ARG;
        }

        const TopoDS_Wire& wire = TopoDS::Wire(shape);
        gp_Pnt pnt(pointX, pointY, pointZ);

        double paramOffset = 0.0;
        for (BRepTools_WireExplorer exp(wire); exp.More(); exp.Next())
        {
            const TopoDS_Edge& edge = TopoDS::Edge(exp.Current());
            Standard_Real fpar = 0, lpar = 0;
            TopLoc_Location aLoc;
            Handle(Geom_Curve) aCurve = BRep_Tool::Curve(edge, aLoc, fpar, lpar);

            if (aCurve.IsNull())
            {
                GProp_GProps gProps;
                BRepGProp::LinearProperties(edge, gProps);
                paramOffset += gProps.Mass();
                continue;
            }

            Standard_Real u = 0;
            if (GeomLib_Tool::Parameter(aCurve, pnt, tolerance, u))
            {
                *outParam = paramOffset + u;
                return XBIM_OK;
            }

            GProp_GProps gProps;
            BRepGProp::LinearProperties(edge, gProps);
            paramOffset += gProps.Mass();
        }

        xbim_set_error("xbim_wire_get_parameter: point is not on the wire");
        return XBIM_ERROR;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_wire_get_parameter: OCCT exception");
        return XBIM_ERROR;
    }
}


/*
 * Trim a wire by parametric range.
 *
 * For single-interval wires (one edge), detects angular conics
 * (circle/ellipse) and applies radianFactor conversion, then builds a
 * trimmed curve.  For multi-interval wires, walks edges with
 * BRepAdaptor_CompCurve, trims first/last edges at the boundaries, and
 * takes intermediate edges whole.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_trimmed(
    XbimContextHandle ctx,
    XbimShapeHandle   wireHandle,
    double            u1,
    double            u2,
    int               sameSense,
    double            tolerance,
    double            radianFactor,
    XbimShapeHandle*  outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_wire_build_trimmed: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!wireHandle)
    {
        xbim_set_error("xbim_wire_build_trimmed: wireHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = wireHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_WIRE)
        {
            xbim_set_error("xbim_wire_build_trimmed: handle is not a wire");
            return XBIM_INVALID_ARG;
        }

        const TopoDS_Wire& wire = TopoDS::Wire(shape);

        BRepAdaptor_CompCurve cc(wire, Standard_True);
        GeomAbs_Shape continuity = cc.Continuity();
        int numIntervals = cc.NbIntervals(continuity);

        double first = u1;
        double last = u2;

        if (numIntervals == 1)
        {
            /* Single-edge wire: detect conic for radian conversion */
            TopoDS_Edge edge;
            Standard_Real uoe;
            cc.Edge((cc.FirstParameter() + cc.LastParameter()) * 0.5, edge, uoe);

            Standard_Real fEdge, lEdge;
            Handle(Geom_Curve) curve = BRep_Tool::Curve(edge, fEdge, lEdge);

            /* Unwrap nested TrimmedCurves to get the basis */
            if (!curve.IsNull())
            {
                Handle(Geom_TrimmedCurve) tc = Handle(Geom_TrimmedCurve)::DownCast(curve);
                while (!tc.IsNull())
                {
                    curve = tc->BasisCurve();
                    tc = Handle(Geom_TrimmedCurve)::DownCast(curve);
                }
            }

            BRepAdaptor_Curve ec(edge);
            const GeomAbs_CurveType ct = ec.GetType();
            const bool isAngularConic = (ct == GeomAbs_Circle) || (ct == GeomAbs_Ellipse);

            Standard_Real f = fEdge;
            Standard_Real l = lEdge;

            if (isAngularConic)
            {
                /* Convert IFC angle parameters to radians */
                Standard_Real a1 = u1 * radianFactor;
                Standard_Real a2 = u2 * radianFactor;

                /* Normalize to 0..2π */
                const Standard_Real per = 2.0 * M_PI;
                auto norm = [&](Standard_Real a) {
                    a = fmod(a, per);
                    if (a < 0) a += per;
                    return a;
                };
                a1 = norm(a1);
                a2 = norm(a2);

                if (!sameSense)
                    std::swap(a1, a2);
                if (a2 <= a1)
                    a2 += per; /* allow wrap across 0 */

                f = a1;
                l = a2;
            }
            else
            {
                /* Use mapped parameters clamped to edge range */
                f = std::max(fEdge, first);
                l = std::min(lEdge, last);
            }

            /* Build trimmed wire if range is valid */
            if (std::abs(f - l) > Precision::Confusion())
            {
                Handle(Geom_TrimmedCurve) trimmed = new Geom_TrimmedCurve(curve, f, l);
                BRepBuilderAPI_MakeWire wm;
                wm.Add(BRepBuilderAPI_MakeEdge(trimmed));

                if (!wm.IsDone())
                {
                    xbim_set_error("xbim_wire_build_trimmed: could not build trimmed wire");
                    return XBIM_NULL_SHAPE;
                }

                *outHandle = xbim_shape_create_from(wm.Wire());
                return *outHandle ? XBIM_OK : XBIM_ERROR;
            }

            /* Range is degenerate — return empty */
            xbim_set_error("xbim_wire_build_trimmed: degenerate trim range");
            return XBIM_NULL_SHAPE;
        }
        else
        {
            /* Multi-edge wire: walk intervals and trim at boundaries */
            BRepBuilderAPI_MakeWire wm;
            TColStd_Array1OfReal res(1, numIntervals + 1);
            cc.Intervals(res, continuity);

            for (Standard_Integer i = 1; i <= numIntervals; i++)
            {
                Standard_Real fp = res.Value(i);
                Standard_Real lp = res.Value(i + 1);

                /* Skip intervals entirely outside the trim range */
                if (first > lp)
                    continue;
                if (last < fp)
                    continue;

                /* Get the edge and its curve for this interval */
                TopoDS_Edge edge;
                Standard_Real uoe;
                cc.Edge(fp, edge, uoe);

                Standard_Real fEdge, lEdge;
                Handle(Geom_Curve) curve = BRep_Tool::Curve(edge, fEdge, lEdge);

                if (curve.IsNull())
                    continue;

                /* Determine if both trim points fall within this single edge */
                if (first > fp && first < lp && last < lp)
                {
                    gp_Pnt pFirst = cc.Value(first);
                    gp_Pnt pLast = cc.Value(last);
                    double maxTol = BRep_Tool::MaxTolerance(edge, TopAbs_VERTEX);
                    double uFirst, uLast;
                    GeomLib_Tool::Parameter(curve, pFirst, maxTol, uFirst);
                    GeomLib_Tool::Parameter(curve, pLast, maxTol, uLast);
                    if (std::abs(uFirst - uLast) > Precision::Confusion())
                    {
                        Handle(Geom_TrimmedCurve) trimmed = new Geom_TrimmedCurve(curve, uFirst, uLast);
                        wm.Add(BRepBuilderAPI_MakeEdge(trimmed));
                    }
                }
                /* Trim from first to end of edge */
                else if (first > fp && first < lp)
                {
                    gp_Pnt pFirst = cc.Value(first);
                    double maxTol = BRep_Tool::MaxTolerance(edge, TopAbs_VERTEX);
                    double uFirst;
                    GeomLib_Tool::Parameter(curve, pFirst, maxTol, uFirst);
                    if (std::abs(uFirst - lEdge) > Precision::Confusion())
                    {
                        Handle(Geom_TrimmedCurve) trimmed = new Geom_TrimmedCurve(curve, uFirst, lEdge);
                        wm.Add(BRepBuilderAPI_MakeEdge(trimmed));
                    }
                    first = -1; /* mark as done */
                }
                /* Trim from start of edge to last */
                else if (last < lp)
                {
                    gp_Pnt pLast = cc.Value(last);
                    double maxTol = BRep_Tool::MaxTolerance(edge, TopAbs_VERTEX);
                    double uLast;
                    GeomLib_Tool::Parameter(curve, pLast, maxTol, uLast);
                    if (std::abs(uLast - fEdge) > Precision::Confusion())
                    {
                        Handle(Geom_TrimmedCurve) trimmed = new Geom_TrimmedCurve(curve, fEdge, uLast);
                        wm.Add(BRepBuilderAPI_MakeEdge(trimmed));
                    }
                }
                else
                {
                    /* Take the whole edge */
                    wm.Add(edge);
                }
            }

            if (!wm.IsDone())
            {
                xbim_set_error("xbim_wire_build_trimmed: no edges after trimming");
                return XBIM_NULL_SHAPE;
            }

            *outHandle = xbim_shape_create_from(wm.Wire());
            return *outHandle ? XBIM_OK : XBIM_ERROR;
        }
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_wire_build_trimmed");
        xbim_set_error("xbim_wire_build_trimmed: OCCT exception");
        return XBIM_ERROR;
    }
}


/*
 * Trim a wire using optional Cartesian point projection.
 *
 * If preferCartesian is true and the points are valid, projects them onto
 * the wire to get parametric positions.  Otherwise falls back to using
 * u1/u2 directly.  Delegates to xbim_wire_build_trimmed for the actual
 * trimming.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_trimmed_by_points(
    XbimContextHandle ctx,
    XbimShapeHandle   wireHandle,
    double            p1X,
    double            p1Y,
    double            p1Z,
    double            p2X,
    double            p2Y,
    double            p2Z,
    double            u1,
    double            u2,
    int               preferCartesian,
    int               sameSense,
    double            tolerance,
    double            radianFactor,
    XbimShapeHandle*  outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_wire_build_trimmed_by_points: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!wireHandle)
    {
        xbim_set_error("xbim_wire_build_trimmed_by_points: wireHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        double first = u1;
        double last = u2;

        if (preferCartesian)
        {
            double param1, param2;
            XbimResult r1 = xbim_wire_get_parameter(wireHandle, p1X, p1Y, p1Z, tolerance, &param1);
            XbimResult r2 = xbim_wire_get_parameter(wireHandle, p2X, p2Y, p2Z, tolerance, &param2);

            if (r1 == XBIM_OK && r2 == XBIM_OK)
            {
                first = param1;
                last = param2;
            }
            else
            {
                xbim_log_warning(ctx, "xbim_wire_build_trimmed_by_points: point projection failed, using parametric values");
            }
        }

        return xbim_wire_build_trimmed(ctx, wireHandle, first, last, sameSense, tolerance, radianFactor, outHandle);
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_wire_build_trimmed_by_points");
        xbim_set_error("xbim_wire_build_trimmed_by_points: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion
