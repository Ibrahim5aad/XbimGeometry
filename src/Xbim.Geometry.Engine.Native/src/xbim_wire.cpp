/*
 * xbim_wire.cpp
 *
 * Implements wire construction and query functions 
 */

#include "xbim_wire.h"
#include "xbim_shape.h"
#include "xbim_curve.h"
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
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepFilletAPI_MakeFillet2d.hxx>
#include <TopTools_Array1OfShape.hxx>
#include <Geom2d_OffsetCurve.hxx>
#include <GCE2d_MakeSegment.hxx>
#include <TopTools_ListOfShape.hxx>


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
            if (p1.IsEqual(p2, ctx->minimumGap))
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


/*
 * Adjusts a shared vertex tolerance so it covers both the existing point
 * and the incoming gap.  Returns true if tolerance was changed.
 * Port of NWireFactory::AdjustVertexTolerance.
 */
static bool adjust_vertex_tolerance(
    const TopoDS_Vertex& vertex,
    const gp_Pnt& existingPt,
    const gp_Pnt& incomingPt,
    double gap)
{
    if (gap <= 0)
        return false;

    BRep_Builder b;
    double currentTol = BRep_Tool::Tolerance(vertex);
    double requiredTol = gap + currentTol;
    if (requiredTol > currentTol)
    {
        b.UpdateVertex(vertex, requiredTol);
        return true;
    }
    return false;
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_from_curves(
    XbimContextHandle         ctx,
    const XbimCurveHandle*    curveHandles,
    int                       numCurves,
    XbimShapeHandle*          outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_wire_build_from_curves: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!curveHandles || numCurves <= 0)
    {
        xbim_set_error("xbim_wire_build_from_curves: curveHandles is NULL or numCurves <= 0");
        return XBIM_INVALID_ARG;
    }

    try
    {
        ShapeFix_Edge toleranceFixer;
        TopTools_SequenceOfShape edges;
        BRep_Builder builder;
        TopoDS_Wire wire;
        TopoDS_Vertex theFirstVertex;
        gp_Pnt theFirstPoint;
        bool isClosed = false;
        bool lastSegmentIsPeriodic = false;
        Handle(Geom_Curve) lastBasisCurve;

        for (int idx = 0; idx < numCurves; ++idx)
        {
            if (!curveHandles[idx])
            {
                xbim_log_warning(ctx, "xbim_wire_build_from_curves: curve handle at index %d is NULL, skipping", idx);
                continue;
            }

            Handle(Geom_Curve) segment = curveHandles[idx]->curve;
            if (segment.IsNull())
            {
                xbim_log_warning(ctx, "xbim_wire_build_from_curves: curve at index %d is null, skipping", idx);
                continue;
            }

            Standard_Real cf = segment->FirstParameter();
            Standard_Real cl = segment->LastParameter();

            /* Determine if basis curve is periodic (circle, ellipse, etc.) */
            bool currentSegmentIsPeriodic = false;
            Handle(Geom_Curve) basisCurve;
            Handle(Geom_TrimmedCurve) trimmedCurve = Handle(Geom_TrimmedCurve)::DownCast(segment);
            while (!trimmedCurve.IsNull())
            {
                basisCurve = trimmedCurve->BasisCurve();
                trimmedCurve = Handle(Geom_TrimmedCurve)::DownCast(basisCurve);
            }
            if (!basisCurve.IsNull())
                currentSegmentIsPeriodic = basisCurve->IsPeriodic();
            else
                currentSegmentIsPeriodic = segment->IsPeriodic();

            TopoDS_Edge anEdge;

            if (edges.Length() == 0)
            {
                /* First edge — create directly */
                BRepBuilderAPI_MakeEdge edgeMaker(segment, cf, cl);
                if (!edgeMaker.IsDone())
                {
                    xbim_log_warning(ctx, "xbim_wire_build_from_curves: failed to rebuild first edge");
                    continue;
                }
                anEdge = edgeMaker.Edge();
                theFirstVertex = TopExp::FirstVertex(anEdge);
                theFirstPoint = BRep_Tool::Pnt(theFirstVertex);
            }
            else
            {
                /* Subsequent edges — connect to the previous edge's last vertex */
                gp_Pnt lastEdgeEndPoint = BRep_Tool::Pnt(TopExp::LastVertex(TopoDS::Edge(edges.Last())));

                gp_Pnt segStartPoint = segment->Value(cf);
                gp_Pnt segEndPoint = segment->Value(cl);

                double gap = segStartPoint.Distance(lastEdgeEndPoint);

                if (gap > ctx->minimumGap)
                {
                    if (gap > 100.0 * ctx->minimumGap)
                    {
                        xbim_set_error("xbim_wire_build_from_curves: segments are not contiguous");
                        return XBIM_ERROR;
                    }
                    xbim_log_warning(ctx,
                        "xbim_wire_build_from_curves: gap %.6g at edge %d exceeds minimumGap %.6g, wire may be discontinuous",
                        gap, idx, ctx->minimumGap);
                }

                /* Periodic/non-periodic transition handling:
                 * When a periodic curve (arc) meets a non-periodic one (line),
                 * rebuild the line geometry to match the arc's endpoint exactly. */
                if (currentSegmentIsPeriodic && !lastSegmentIsPeriodic)
                {
                    Handle(Geom_Line) line = Handle(Geom_Line)::DownCast(lastBasisCurve);
                    if (!line.IsNull())
                    {
                        gp_Pnt lastSegStartPoint = BRep_Tool::Pnt(TopExp::FirstVertex(TopoDS::Edge(edges.Last())));
                        gp_Vec dir(lastSegStartPoint, segStartPoint);
                        double dirMag = dir.Magnitude();
                        if (dirMag > Precision::Confusion())
                        {
                            gp_Lin newLine(lastSegStartPoint, dir);
                            Handle(Geom_Line) hLine = new Geom_Line(newLine);
                            Handle(Geom_TrimmedCurve) newSegment = new Geom_TrimmedCurve(hLine, 0, dirMag);
                            builder.UpdateVertex(TopExp::LastVertex(TopoDS::Edge(edges.Last())), segStartPoint, ctx->precision);
                            BRepBuilderAPI_MakeEdge edgeMaker(newSegment,
                                TopExp::FirstVertex(TopoDS::Edge(edges.Last())),
                                TopExp::LastVertex(TopoDS::Edge(edges.Last())));
                            if (edgeMaker.IsDone())
                            {
                                auto newEdge = edgeMaker.Edge();
                                edges.Remove(edges.Size());
                                edges.Append(newEdge);
                                lastEdgeEndPoint = segStartPoint;
                                gap = 0;
                            }
                        }
                    }
                }
                if (lastSegmentIsPeriodic && !currentSegmentIsPeriodic)
                {
                    Handle(Geom_Line) line = Handle(Geom_Line)::DownCast(basisCurve);
                    if (!line.IsNull())
                    {
                        segStartPoint = lastEdgeEndPoint;
                        gp_Vec dir(segStartPoint, segEndPoint);
                        double dirMag = dir.Magnitude();
                        if (dirMag > Precision::Confusion())
                        {
                            gp_Lin newLine(segStartPoint, dir);
                            Handle(Geom_Line) hLine = new Geom_Line(newLine);
                            segment = new Geom_TrimmedCurve(hLine, 0, dirMag);
                            cf = 0;
                            cl = dirMag;
                            gap = 0;
                        }
                    }
                }

                adjust_vertex_tolerance(TopExp::LastVertex(TopoDS::Edge(edges.Last())),
                    lastEdgeEndPoint, segStartPoint, gap);

                /* Check for wire closure */
                TopoDS_Vertex segEndVertex;
                if (idx == numCurves - 1 && theFirstPoint.Distance(segEndPoint) < ctx->minimumGap)
                {
                    isClosed = true;
                    double closingGap = segEndPoint.Distance(theFirstPoint);
                    adjust_vertex_tolerance(TopExp::FirstVertex(TopoDS::Edge(edges.First())),
                        theFirstPoint, segEndPoint, closingGap);
                }
                else
                {
                    builder.MakeVertex(segEndVertex, segEndPoint, ctx->precision);
                }

                /* Build edge with shared vertices */
                BRepBuilderAPI_MakeEdge edgeMaker(segment,
                    TopExp::LastVertex(TopoDS::Edge(edges.Last())),
                    isClosed ? TopExp::FirstVertex(TopoDS::Edge(edges.First())) : segEndVertex,
                    cf, cl);

                if (!edgeMaker.IsDone())
                {
                    xbim_log_warning(ctx, "xbim_wire_build_from_curves: failed to rebuild edge %d (error %d)",
                        idx, edgeMaker.Error());
                    continue;
                }
                anEdge = edgeMaker.Edge();
            }

            edges.Append(anEdge);
            lastBasisCurve = basisCurve;
            lastSegmentIsPeriodic = currentSegmentIsPeriodic;
        }

        if (edges.Length() == 0)
        {
            xbim_set_error("xbim_wire_build_from_curves: no valid edges produced");
            return XBIM_NULL_SHAPE;
        }

        /* Assemble wire from edges with vertex tolerance fix */
        builder.MakeWire(wire);
        for (int i = 1; i <= edges.Length(); ++i)
        {
            toleranceFixer.FixVertexTolerance(TopoDS::Edge(edges(i)));
            builder.Add(wire, TopoDS::Edge(edges(i)));
        }
        if (isClosed)
            wire.Closed(true);

        if (wire.IsNull())
        {
            xbim_set_error("xbim_wire_build_from_curves: resulting wire is null");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(wire);
        if (!*outHandle)
        {
            xbim_set_error("xbim_wire_build_from_curves: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_wire_build_from_curves");
        xbim_set_error("xbim_wire_build_from_curves: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_polyline(
    XbimContextHandle ctx,
    const double*     pointsXYZ,
    int               numPoints,
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

                if (segLen < ctx->precision + vtxTol)
                {
                    /* Merge into the previous vertex */
                    gp_Pnt midPt = lastPt.Translated(edgeVec.Divided(2));
                    builder.UpdateVertex(lastVtx, midPt, segLen + vtxTol);
                    continue;
                }
            }

            TopoDS_Vertex v;
            builder.MakeVertex(v, pt, ctx->precision);
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
        bool closed = (vertices.Length() > 2) && (firstPt.Distance(lastPt) <= ctx->precision);

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

            if (p1.Distance(p2) < ctx->minimumGap)
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

            if (pLast.Distance(pFirst) >= ctx->minimumGap)
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
                if (gap > ctx->minimumGap)
                {
                    xbim_log_warning(ctx,
                        "xbim_wire_build_from_2d_curves: segment %d gap %.6f exceeds minimumGap %.6f",
                        i, gap, ctx->minimumGap);
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
                if (i == numCurves - 1 && theFirstPoint.Distance(segEndPoint) < ctx->minimumGap)
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
                    builder.MakeVertex(segEndVertex, segEndPoint, ctx->precision);
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
            bool ok = BRepLib::BuildCurve3d(anEdge, ctx->precision);
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

XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_centerline_profile(
    XbimContextHandle   ctx,
    XbimCurve2dHandle   centreLineHandle,
    double              thickness,
    XbimShapeHandle*    outWire)
{
    xbim_clear_error();

    if (!outWire)
    {
        xbim_set_error("xbim_wire_build_centerline_profile: outWire is NULL");
        return XBIM_INVALID_ARG;
    }
    *outWire = nullptr;

    if (!centreLineHandle || centreLineHandle->curve.IsNull())
    {
        xbim_set_error("xbim_wire_build_centerline_profile: centreLineHandle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    if (thickness <= 0)
    {
        xbim_set_error("xbim_wire_build_centerline_profile: thickness must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        const Handle(Geom2d_Curve)& centreLine = centreLineHandle->curve;
        double halfThickness = thickness / 2.0;

        /* Build two offset curves at +/- halfThickness */
        Handle(Geom2d_OffsetCurve) aCurve =
            new Geom2d_OffsetCurve(centreLine, halfThickness);
        Handle(Geom2d_OffsetCurve) bCurve =
            new Geom2d_OffsetCurve(centreLine, -halfThickness);

        /* Evaluate the four endpoints */
        gp_Pnt2d aStart, aEnd, bStart, bEnd;
        aCurve->D0(aCurve->FirstParameter(), aStart);
        aCurve->D0(aCurve->LastParameter(), aEnd);
        bCurve->D0(bCurve->FirstParameter(), bStart);
        bCurve->D0(bCurve->LastParameter(), bEnd);

        /* Build 4 edges in CCW order around the ribbon:
           1. Outer arc forward:   aStart → aEnd
           2. End cap:             aEnd   → bEnd
           3. Inner arc reversed:  bEnd   → bStart
           4. Start cap:           bStart → aStart */
        BRepBuilderAPI_MakeEdge2d edgeA(aCurve);
        if (!edgeA.IsDone())
        {
            xbim_set_error("xbim_wire_build_centerline_profile: failed to build edge from offset curve A");
            return XBIM_ERROR;
        }

        GCE2d_MakeSegment lineEndCap(aEnd, bEnd);
        if (!lineEndCap.IsDone())
        {
            xbim_set_error("xbim_wire_build_centerline_profile: failed to build end cap line");
            return XBIM_ERROR;
        }
        BRepBuilderAPI_MakeEdge2d edgeEndCap(lineEndCap.Value());

        BRepBuilderAPI_MakeEdge2d edgeB(bCurve);
        if (!edgeB.IsDone())
        {
            xbim_set_error("xbim_wire_build_centerline_profile: failed to build edge from offset curve B");
            return XBIM_ERROR;
        }

        GCE2d_MakeSegment lineStartCap(bStart, aStart);
        if (!lineStartCap.IsDone())
        {
            xbim_set_error("xbim_wire_build_centerline_profile: failed to build start cap line");
            return XBIM_ERROR;
        }
        BRepBuilderAPI_MakeEdge2d edgeStartCap(lineStartCap.Value());

        /* Generate 3D curves for all edges */
        BRepLib::BuildCurve3d(edgeA.Edge(), ctx->precision);
        BRepLib::BuildCurve3d(edgeEndCap.Edge(), ctx->precision);
        BRepLib::BuildCurve3d(edgeB.Edge(), ctx->precision);
        BRepLib::BuildCurve3d(edgeStartCap.Edge(), ctx->precision);

        /* Assemble wire: reverse inner arc edge orientation (not the curve)
           so the edge traverses bEnd→bStart without modifying the offset curve */
        BRepBuilderAPI_MakeWire wireMaker;
        wireMaker.Add(edgeA.Edge());
        wireMaker.Add(edgeEndCap.Edge());
        wireMaker.Add(TopoDS::Edge(edgeB.Edge().Reversed()));
        wireMaker.Add(edgeStartCap.Edge());

        if (!wireMaker.IsDone())
        {
            xbim_set_error("xbim_wire_build_centerline_profile: failed to assemble wire");
            return XBIM_ERROR;
        }

        TopoDS_Wire wire = wireMaker.Wire();
        *outWire = xbim_shape_create_from(wire);
        if (!*outWire)
        {
            xbim_set_error("xbim_wire_build_centerline_profile: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_wire_build_centerline_profile");
        xbim_set_error("xbim_wire_build_centerline_profile: OCCT exception");
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


XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_is_planar(
    XbimShapeHandle wireHandle,
    double          tolerance,
    int*            outPlanar)
{
    xbim_clear_error();

    if (!outPlanar)
    {
        xbim_set_error("xbim_wire_is_planar: outPlanar is NULL");
        return XBIM_INVALID_ARG;
    }
    *outPlanar = 0;

    if (!wireHandle)
    {
        xbim_set_error("xbim_wire_is_planar: wireHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = wireHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_WIRE)
        {
            xbim_set_error("xbim_wire_is_planar: handle is not a wire");
            return XBIM_INVALID_ARG;
        }

        const TopoDS_Wire& wire = TopoDS::Wire(shape);
        BRepBuilderAPI_MakeFace faceMaker(wire, Standard_True);
        *outPlanar = faceMaker.IsDone() ? XBIM_TRUE : XBIM_FALSE;

        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        /* Exception means not planar */
        *outPlanar = XBIM_FALSE;
        return XBIM_OK;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_get_ordered_points(
    XbimShapeHandle wireHandle,
    double*         outCoords,
    int*            outCount)
{
    xbim_clear_error();

    if (!outCount)
    {
        xbim_set_error("xbim_wire_get_ordered_points: outCount is NULL");
        return XBIM_INVALID_ARG;
    }

    if (!wireHandle)
    {
        xbim_set_error("xbim_wire_get_ordered_points: wireHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = wireHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_WIRE)
        {
            xbim_set_error("xbim_wire_get_ordered_points: handle is not a wire");
            return XBIM_INVALID_ARG;
        }

        const TopoDS_Wire& wire = TopoDS::Wire(shape);

        /* Collect ordered points: start vertex of each edge + end of last */
        std::vector<gp_Pnt> pts;
        TopoDS_Vertex lastVertex;
        for (BRepTools_WireExplorer wEx(wire); wEx.More(); wEx.Next())
        {
            pts.push_back(BRep_Tool::Pnt(wEx.CurrentVertex()));
            /* Track the end vertex of the current (last) edge */
            TopoDS_Vertex v1, v2;
            TopExp::Vertices(wEx.Current(), v1, v2);
            if (!v1.IsNull() && !v2.IsNull())
                lastVertex = v1.IsSame(wEx.CurrentVertex()) ? v2 : v1;
            else if (!v2.IsNull())
                lastVertex = v2;
        }

        /* Add the end vertex of the last edge */
        if (!lastVertex.IsNull())
            pts.push_back(BRep_Tool::Pnt(lastVertex));

        if (!outCoords)
        {
            /* Count-only mode */
            *outCount = (int)pts.size();
            return XBIM_OK;
        }

        int n = std::min((int)pts.size(), *outCount);
        for (int i = 0; i < n; i++)
        {
            outCoords[i * 3 + 0] = pts[i].X();
            outCoords[i * 3 + 1] = pts[i].Y();
            outCoords[i * 3 + 2] = pts[i].Z();
        }
        *outCount = n;

        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_wire_get_ordered_points: OCCT exception");
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
                Standard_Real a1 = u1 * ctx->radianFactor;
                Standard_Real a2 = u2 * ctx->radianFactor;

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
 * Trim a wire by arc-length positions.
 *
 * Walks edges via BRepTools_WireExplorer, accumulates geometric
 * arc-lengths via GCPnts_AbscissaPoint::Length.  For the edge
 * containing arcStart, trims the curve from the start offset.
 * For the edge containing arcEnd, trims to the end offset.
 * Edges fully inside the range are taken whole; edges outside
 * are skipped.  Builds the result wire from collected edges.
 *
 *   ctx       – a valid context handle (used for logging; may be NULL)
 *   wireHandle – the basis wire to trim
 *   arcStart  – start position in arc-length units from wire start
 *   arcEnd    – end position in arc-length units from wire start
 *   outHandle – receives the trimmed wire
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_trimmed_by_length(
    XbimContextHandle ctx,
    XbimShapeHandle   wireHandle,
    double            arcStart,
    double            arcEnd,
    XbimShapeHandle*  outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_wire_build_trimmed_by_length: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!wireHandle)
    {
        xbim_set_error("xbim_wire_build_trimmed_by_length: wireHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    if (arcEnd <= arcStart)
    {
        xbim_set_error("xbim_wire_build_trimmed_by_length: arcEnd must be greater than arcStart");
        return XBIM_INVALID_ARG;
    }

    try
    {
        const TopoDS_Shape& shape = wireHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_WIRE)
        {
            xbim_set_error("xbim_wire_build_trimmed_by_length: handle is not a wire");
            return XBIM_INVALID_ARG;
        }

        const TopoDS_Wire& wire = TopoDS::Wire(shape);
        BRepBuilderAPI_MakeWire wm;
        double accumulated = 0.0;

        for (BRepTools_WireExplorer exp(wire); exp.More(); exp.Next())
        {
            const TopoDS_Edge& edge = exp.Current();
            Standard_Real fEdge, lEdge;
            Handle(Geom_Curve) curve = BRep_Tool::Curve(edge, fEdge, lEdge);

            if (curve.IsNull())
                continue;

            BRepAdaptor_Curve adaptor(edge);
            double edgeLen = GCPnts_AbscissaPoint::Length(adaptor, fEdge, lEdge);
            double edgeStart = accumulated;
            double edgeEnd = accumulated + edgeLen;

            /* Skip edges entirely before the trim start */
            if (edgeEnd <= arcStart + Precision::Confusion())
            {
                accumulated = edgeEnd;
                continue;
            }

            /* Stop if we've passed the trim end */
            if (edgeStart >= arcEnd - Precision::Confusion())
                break;

            /* Determine trim parameters within this edge */
            double trimFrac1 = 0.0; /* fraction from edge start */
            double trimFrac2 = 1.0; /* fraction to edge end */

            bool needTrimStart = (arcStart > edgeStart + Precision::Confusion());
            bool needTrimEnd   = (arcEnd < edgeEnd - Precision::Confusion());

            if (needTrimStart)
                trimFrac1 = (arcStart - edgeStart) / edgeLen;
            if (needTrimEnd)
                trimFrac2 = (arcEnd - edgeStart) / edgeLen;

            if (!needTrimStart && !needTrimEnd)
            {
                /* Take the whole edge */
                wm.Add(edge);
            }
            else
            {
                /* Map fractions to curve parameters */
                Standard_Real paramRange = lEdge - fEdge;
                Standard_Real p1 = fEdge + trimFrac1 * paramRange;
                Standard_Real p2 = fEdge + trimFrac2 * paramRange;

                /* For better accuracy on non-linear curves, use
                 * GCPnts_AbscissaPoint to find exact parameters */
                if (needTrimStart)
                {
                    double targetLen = arcStart - edgeStart;
                    GCPnts_AbscissaPoint finder(adaptor, targetLen, fEdge);
                    if (finder.IsDone())
                        p1 = finder.Parameter();
                }
                if (needTrimEnd)
                {
                    double targetLen = arcEnd - edgeStart;
                    GCPnts_AbscissaPoint finder(adaptor, targetLen, fEdge);
                    if (finder.IsDone())
                        p2 = finder.Parameter();
                }

                if (std::abs(p2 - p1) > Precision::Confusion())
                {
                    Handle(Geom_TrimmedCurve) trimmed = new Geom_TrimmedCurve(curve, p1, p2);
                    BRepBuilderAPI_MakeEdge edgeMaker(trimmed);
                    if (edgeMaker.IsDone())
                        wm.Add(edgeMaker.Edge());
                }
            }

            accumulated = edgeEnd;

            /* If we've covered the trim end, stop */
            if (arcEnd <= edgeEnd + Precision::Confusion())
                break;
        }

        if (!wm.IsDone())
        {
            xbim_set_error("xbim_wire_build_trimmed_by_length: no edges after trimming");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(wm.Wire());
        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_wire_build_trimmed_by_length");
        xbim_set_error("xbim_wire_build_trimmed_by_length: OCCT exception");
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
            XbimResult r1 = xbim_wire_get_parameter(wireHandle, p1X, p1Y, p1Z, ctx->precision, &param1);
            XbimResult r2 = xbim_wire_get_parameter(wireHandle, p2X, p2Y, p2Z, ctx->precision, &param2);

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

        return xbim_wire_build_trimmed(ctx, wireHandle, first, last, sameSense, outHandle);
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_wire_build_trimmed_by_points");
        xbim_set_error("xbim_wire_build_trimmed_by_points: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_fillet(
    XbimContextHandle ctx,
    XbimShapeHandle   wireHandle,
    double            filletRadius,
    XbimShapeHandle*  outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_wire_fillet: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!wireHandle)
    {
        xbim_set_error("xbim_wire_fillet: wireHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (filletRadius <= 0)
    {
        xbim_set_error("xbim_wire_fillet: filletRadius must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        const TopoDS_Shape& shape = wireHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_WIRE)
        {
            xbim_set_error("xbim_wire_fillet: handle is not a wire");
            return XBIM_INVALID_ARG;
        }

        const TopoDS_Wire& wire = TopoDS::Wire(shape);
        Standard_Integer nbEdges = wire.NbChildren();
        if (nbEdges < 2)
        {
            // Nothing to fillet — return a copy of the original wire
            *outHandle = xbim_shape_create_from(wire);
            return XBIM_OK;
        }

        // Collect all edges and vertices
        TopTools_Array1OfShape edges(1, nbEdges);
        TopTools_Array1OfShape vertices(1, nbEdges);
        // Filleted result: each pair can produce up to 3 edges (trimmed + fillet + trimmed)
        TopTools_Array1OfShape filleted(1, nbEdges * 2);
        Standard_Integer nb = 0;
        for (BRepTools_WireExplorer edgeExp(wire); edgeExp.More(); edgeExp.Next())
        {
            nb++;
            edges(nb) = TopoDS::Edge(edgeExp.Current());
            vertices(nb) = TopoDS::Vertex(edgeExp.CurrentVertex());
        }

        // Fillet each consecutive edge pair
        int totalEdges = 1;
        for (int i = 1; i < nbEdges; i++)
        {
            BRepBuilderAPI_MakeWire filletWireMaker;
            filletWireMaker.Add(TopoDS::Edge(edges(i)));
            filletWireMaker.Add(TopoDS::Edge(edges(i + 1)));
            BRepBuilderAPI_MakeFace faceMaker(filletWireMaker.Wire());
            BRepFilletAPI_MakeFillet2d filleter(faceMaker.Face());
            filleter.AddFillet(TopoDS::Vertex(vertices(i + 1)), filletRadius);
            filleter.Build();
            if (filleter.IsDone() && filleter.NbFillet() > 0)
            {
                const TopTools_SequenceOfShape& fillets = filleter.FilletEdges();
                filleted(2 * i - 1) = filleter.DescendantEdge(TopoDS::Edge(edges(i)));
                edges(i) = filleted(2 * i - 1);
                filleted(2 * i) = fillets(1);
                filleted(2 * i + 1) = filleter.DescendantEdge(TopoDS::Edge(edges(i + 1)));
                edges(i + 1) = filleted(2 * i + 1);
                totalEdges += 2;
            }
            else
            {
                xbim_log_warning(ctx, "xbim_wire_fillet: failed to fillet edge pair, skipping");
                filleted(2 * i - 1) = edges(i);
                filleted(2 * i) = edges(i + 1);
                totalEdges++;
            }
        }

        // If closed, also fillet the first/last vertex
        if (wire.Closed() && nbEdges > 1)
        {
            BRepBuilderAPI_MakeWire filletWireMaker;
            filletWireMaker.Add(TopoDS::Edge(edges(1)));
            filletWireMaker.Add(TopoDS::Edge(edges(nbEdges)));
            BRepBuilderAPI_MakeFace faceMaker(filletWireMaker.Wire());
            BRepFilletAPI_MakeFillet2d filleter(faceMaker.Face());
            filleter.AddFillet(TopoDS::Vertex(vertices(1)), filletRadius);
            filleter.Build();
            if (filleter.IsDone() && filleter.NbFillet() > 0)
            {
                const TopTools_SequenceOfShape& fillets = filleter.FilletEdges();
                filleted(2 * nbEdges - 1) = filleter.DescendantEdge(TopoDS::Edge(edges(nbEdges)));
                filleted(2 * nbEdges) = fillets(1);
                filleted(1) = filleter.DescendantEdge(TopoDS::Edge(edges(1)));
                totalEdges++;
            }
            else
            {
                xbim_log_warning(ctx, "xbim_wire_fillet: failed to close fillet at first/last vertex");
            }
        }

        // Build the final wire from filleted edges
        BRepBuilderAPI_MakeWire wireMaker;
        for (int i = 1; i <= totalEdges; i++)
        {
            if (!TopoDS::Edge(filleted(i)).IsNull())
                wireMaker.Add(TopoDS::Edge(filleted(i)));
        }

        if (!wireMaker.IsDone())
        {
            xbim_set_error("xbim_wire_fillet: failed to build filleted wire");
            return XBIM_ERROR;
        }

        *outHandle = xbim_shape_create_from(wireMaker.Wire());
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_wire_fillet");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_wire_fillet: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion
