#include "xbim_advanced_brep_builder.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepCheck_Shell.hxx>
#include <BRepFill.hxx>
#include <BRepFill_Filling.hxx>
#include <BRep_Tool.hxx>
#include <Geom_Line.hxx>
#include <GeomAdaptor_Curve.hxx>
#include <GCPnts_AbscissaPoint.hxx>
#include <Extrema_ExtPC.hxx>
#include <Precision.hxx>
#include <ShapeFix_Edge.hxx>
#include <ShapeFix_Wire.hxx>
#include <ShapeFix_Face.hxx>
#include <ShapeFix_Shape.hxx>
#include <ShapeFix_Shell.hxx>
#include <ShapeFix_Solid.hxx>
#include <ShapeAnalysis.hxx>
#include <ShapeAnalysis_Surface.hxx>
#include <Standard_Failure.hxx>
#include <BRepTools_WireExplorer.hxx>
#include <TopExp.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS.hxx>
#include <Geom_TrimmedCurve.hxx>
#include <Geom_Plane.hxx>


/* ──────────────────────────── helpers ──────────────────────────── */

/*
 * Locate a point on a curve — same as the old LocatePointOnCurve.
 * Returns true if the point was found within tolerance, populating
 * parameter and distance.
 */
static bool locate_point_on_curve(
    const Handle(Geom_Curve)& C,
    const TopoDS_Vertex& V,
    double tolerance,
    double& p,
    double& distance)
{
    double Eps2 = tolerance * tolerance;
    gp_Pnt P = BRep_Tool::Pnt(V);
    GeomAdaptor_Curve GAC(C);

    gp_Pnt P1 = GAC.Value(GAC.FirstParameter());
    gp_Pnt P2 = GAC.Value(GAC.LastParameter());
    double D1 = P1.SquareDistance(P);
    double D2 = P2.SquareDistance(P);

    if ((D1 < D2) && (D1 <= Eps2))
    {
        p = GAC.FirstParameter();
        distance = sqrt(D1);
        return true;
    }
    else if ((D2 < D1) && (D2 <= Eps2))
    {
        p = GAC.LastParameter();
        distance = sqrt(D2);
        return true;
    }

    Extrema_ExtPC extrema(P, GAC);
    if (extrema.IsDone())
    {
        int index = 0, n = extrema.NbExt();
        double Dist2 = RealLast();

        for (int i = 1; i <= n; i++)
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
            p = extrema.Point(index).Parameter();
            distance = sqrt(Dist2);
            return true;
        }
    }
    return false;
}


/*
 * Check whether all edges of a wire lie within tolerance of a face's surface.
 */
static bool within_tolerance(
    const TopoDS_Wire& wire,
    const TopoDS_Face& face,
    double tolerance)
{
    try
    {
        /* Check that wire edges lie on the face surface within tolerance * 10.
         * Samples each edge at start, midpoint, and end, then projects onto
         * the surface. */
        Handle(Geom_Surface) surface = BRep_Tool::Surface(face);
        if (surface.IsNull())
            return false;

        double tol10 = tolerance * 10;
        ShapeAnalysis_Surface sas(surface);

        for (TopExp_Explorer edgeExp(wire, TopAbs_EDGE); edgeExp.More(); edgeExp.Next())
        {
            double first, last;
            Handle(Geom_Curve) curve = BRep_Tool::Curve(
                TopoDS::Edge(edgeExp.Current()), first, last);
            if (curve.IsNull())
                return false;

            /* Sample start, midpoint, and end of the edge */
            double params[3] = { first, (first + last) * 0.5, last };
            for (int i = 0; i < 3; i++)
            {
                gp_Pnt pnt = curve->Value(params[i]);
                gp_Pnt2d uv = sas.ValueOfUV(pnt, tol10);
                gp_Pnt projected = surface->Value(uv.X(), uv.Y());
                if (pnt.Distance(projected) > tol10)
                    return false;
            }
        }
        return true;
    }
    catch (...)
    {
        return false;
    }
}

/*
 * Try to rebuild a face surface from the outer wire edges when the IFC-defined
 * surface doesn't match the wire geometry (ruled surface fallback).
 *
 * For 4-edge loops (2 curves + 2 lines): BRepFill::Face on the 2 non-line edges.
 * Fallback: BRepFill_Filling fitted to all wire edges.
 */
static bool rebuild_ruled_surface(
    XbimAdvancedBrepBuilder_& b,
    TopoDS_Wire& outerWire,   /* may be updated with pcurves fixed for new surface */
    TopoDS_Face& outFace)
{
    /* Collect edges in wire traversal order (BRepTools_WireExplorer respects
     * vertex connectivity, unlike TopExp_Explorer which may give arbitrary order).
     */
    std::vector<TopoDS_Edge> curveEdges;
    std::vector<TopoDS_Edge> allEdges;

    for (BRepTools_WireExplorer exp(outerWire); exp.More(); exp.Next())
    {
        TopoDS_Edge edge = exp.Current();
        allEdges.push_back(edge);

        double f, l;
        Handle(Geom_Curve) c3d = BRep_Tool::Curve(edge, f, l);
        if (!c3d.IsNull() && Handle(Geom_Line)::DownCast(c3d).IsNull())
            curveEdges.push_back(edge);
    }

    /* Helper: fix wire pcurves against a new surface and update outerWire */
    auto fix_wire_for_surface = [&](const TopoDS_Face& face)
    {
        ShapeFix_Wire wf(outerWire, face, b.ctx->minimumGap);
        if (wf.Perform())
            outerWire = wf.Wire();
    };

    /* Try BRepFill::Face for 4-edge loops with at least 2 non-line edges.
     *
     * The old engine iterates edges and picks the first 2 non-line curves,
     * reversing the second one, then breaks. This works even when the loop
     * has 3 non-line edges (e.g. BSpline + Circle + BSpline + Line). */
    if (allEdges.size() == 4 && curveEdges.size() >= 2)
    {
        try
        {
            TopoDS_Edge e2 = TopoDS::Edge(curveEdges[1].Reversed());
            TopoDS_Face ruledFace = BRepFill::Face(curveEdges[0], e2);
            if (!ruledFace.IsNull())
            {
                /* Use EmptyCopied to strip BRepFill's wires, keeping just the
                   surface */
                TopoDS_Face emptyFace = TopoDS::Face(ruledFace.EmptyCopied());
                fix_wire_for_surface(emptyFace);
                outFace = emptyFace;
                return true;
            }
        }
        catch (const Standard_Failure& sf)
        {
            xbim_log_warning(b.ctx, "BRepFill::Face failed: %s",
                sf.GetMessageString() ? sf.GetMessageString() : "unknown");
        }
    }

    /* Fallback: BRepFill_Filling from all wire edges */
    try
    {
        BRepFill_Filling filling;
        for (const auto& edge : allEdges)
            filling.Add(edge, GeomAbs_C0);
        filling.Build();
        if (filling.IsDone())
        {
            TopoDS_Face filledFace = filling.Face();
            fix_wire_for_surface(filledFace);
            outFace = filledFace;
            return !outFace.IsNull();
        }
    }
    catch (const Standard_Failure& sf)
    {
        xbim_log_warning(b.ctx, "BRepFill_Filling failed: %s",
            sf.GetMessageString() ? sf.GetMessageString() : "unknown");
    }

    return false;
}


/* ──────────────────────────── GetVertex ──────────────────────────── */

TopoDS_Vertex xbim_brep_get_vertex(
    XbimAdvancedBrepBuilder_& b,
    int label,
    double x, double y, double z)
{
    if (b.vertexCache.IsBound(label))
        return TopoDS::Vertex(b.vertexCache.Find(label));

    TopoDS_Vertex vertex;
    gp_Pnt pnt(x, y, z);
    b.builder.MakeVertex(vertex, pnt, Precision::Confusion());
    b.vertexCache.Bind(label, vertex);
    return vertex;
}


/* ──────────────────────────── BuildOrientEdge ──────────────────────────── */

/*
 * Find the curve geometry for an edge label.
 * The curves are registered via xbim_advanced_brep_add_edge_curve.
 */
static Handle(Geom_Curve) find_edge_curve(
    XbimAdvancedBrepBuilder_& b,
    int edgeLabel)
{
    const Handle(Geom_Curve)* pCurve = b.curveMap.Seek(edgeLabel);
    if (pCurve)
        return *pCurve;
    return Handle(Geom_Curve)();
}


TopoDS_Edge xbim_brep_build_orient_edge(
    XbimAdvancedBrepBuilder_& b,
    int edgeLabel,
    int startVertexLabel,
    int endVertexLabel,
    int sameSense)
{
    /* Return cached edge if already built (with correct orientation) */
    if (b.edgeCache.IsBound(edgeLabel))
    {
        TopoDS_Edge existingEdge = TopoDS::Edge(b.edgeCache.Find(edgeLabel));
        if (!sameSense)
            return TopoDS::Edge(existingEdge.Reversed());
        else
            return existingEdge;
    }

    Handle(Geom_Curve) sharedEdgeGeom = find_edge_curve(b, edgeLabel);
    if (sharedEdgeGeom.IsNull())
    {
        xbim_log_warning(b.ctx, "Edge curve geometry not found for edge #%d", edgeLabel);
        return TopoDS_Edge();
    }

    /* Get shared vertices */
    TopoDS_Vertex startV, endV;
    if (b.vertexCache.IsBound(startVertexLabel))
        startV = TopoDS::Vertex(b.vertexCache.Find(startVertexLabel));
    if (b.vertexCache.IsBound(endVertexLabel))
        endV = TopoDS::Vertex(b.vertexCache.Find(endVertexLabel));

    if (startV.IsNull() || endV.IsNull())
    {
        xbim_log_warning(b.ctx, "Vertices not found for edge #%d (start=%d, end=%d)",
            edgeLabel, startVertexLabel, endVertexLabel);
        return TopoDS_Edge();
    }

    /* Fuse geometrically identical vertices — matches old engine IsGeometricallySame.
     * Some IFC files (e.g. Revit exports) define two separate VertexPoint entities
     * with the same CartesianPoint for the start and end of a closed curve.
     * Both vertices project to the same curve parameter, causing
     * BRepBuilderAPI_MakeEdge to fail with DifferentPointsOnClosedCurve.
     * Fusing them enables the closed-edge path below. */
    if (sharedEdgeGeom->IsClosed() && !startV.IsSame(endV))
    {
        gp_Pnt p1 = BRep_Tool::Pnt(startV);
        gp_Pnt p2 = BRep_Tool::Pnt(endV);
        if (p1.Distance(p2) <= b.ctx->precision)
            endV = startV;
    }

    ShapeFix_Edge edgeFixer;
    TopoDS_Edge topoEdgeCurve;

    /* If the curve is closed and start/end vertices are the same,
     * build the entire closed edge */
    if (sharedEdgeGeom->IsClosed() && startV.IsSame(endV))
    {
        double f = sharedEdgeGeom->FirstParameter();
        double l = sharedEdgeGeom->LastParameter();
        BRepBuilderAPI_MakeEdge edgeMaker(sharedEdgeGeom, startV, endV, f, l);
        if (edgeMaker.IsDone())
        {
            topoEdgeCurve = edgeMaker.Edge();
        }
        else
        {
            xbim_log_warning(b.ctx, "Failed to create closed edge #%d", edgeLabel);
            return TopoDS_Edge();
        }
    }
    else
    {
        double trimParam1, trimParam2;
        double trim1Tolerance, trim2Tolerance;

        bool foundP1 = locate_point_on_curve(sharedEdgeGeom, startV,
            b.ctx->minimumGap, trimParam1, trim1Tolerance);
        bool foundP2 = locate_point_on_curve(sharedEdgeGeom, endV,
            b.ctx->minimumGap, trimParam2, trim2Tolerance);

        if (!foundP1)
        {
            xbim_log_warning(b.ctx,
                "Failed to project vertex to edge geometry: #%d, start point assumed",
                edgeLabel);
            trimParam1 = sharedEdgeGeom->FirstParameter();
            trim1Tolerance = b.ctx->minimumGap;
        }
        if (!foundP2)
        {
            xbim_log_warning(b.ctx,
                "Failed to project vertex to edge geometry: #%d, end point assumed",
                edgeLabel);
            trimParam2 = sharedEdgeGeom->LastParameter();
            trim2Tolerance = b.ctx->minimumGap;
        }

        double currentStartTol = BRep_Tool::Tolerance(startV);
        double currentEndTol = BRep_Tool::Tolerance(endV);

        if (trim1Tolerance > currentStartTol)
            b.builder.UpdateVertex(startV, trim1Tolerance);
        if (trim2Tolerance > currentEndTol)
            b.builder.UpdateVertex(endV, trim2Tolerance);

        BRepBuilderAPI_MakeEdge edgeMaker(sharedEdgeGeom, startV, endV,
            trimParam1, trimParam2);
        if (!edgeMaker.IsDone())
        {
            xbim_log_warning(b.ctx, "Failed to create edge #%d (error %d)",
                edgeLabel, (int)edgeMaker.Error());
            return TopoDS_Edge();
        }

        topoEdgeCurve = edgeMaker.Edge();
        edgeFixer.FixVertexTolerance(topoEdgeCurve);
    }

    /* Cache the edge */
    b.edgeCache.Bind(edgeLabel, topoEdgeCurve);

    if (!sameSense)
        topoEdgeCurve = TopoDS::Edge(topoEdgeCurve.Reversed());

    return topoEdgeCurve;
}


/* ──────────────────────────── BuildLoopWire ──────────────────────────── */

void xbim_brep_build_loop_wire(
    XbimAdvancedBrepBuilder_& b,
    const XbimBrepBoundData& boundData,
    const TopoDS_Face& face,
    bool buildRuledSurface,
    TopoDS_Wire& outerLoop,
    std::vector<TopoDS_Wire>& innerLoops)
{
    TopoDS_Wire loopWire;
    b.builder.MakeWire(loopWire);

    std::vector<TopoDS_Edge> loopEdges;
    ShapeFix_Edge edgeFixer;

    for (const auto& edgeData : boundData.edges)
    {
        TopoDS_Edge topoEdge = xbim_brep_build_orient_edge(
            b, edgeData.edgeLabel,
            edgeData.startVertexLabel,
            edgeData.endVertexLabel,
            edgeData.sameSense);

        if (topoEdge.IsNull())
            continue;

        if (!buildRuledSurface)
            edgeFixer.FixAddPCurve(topoEdge, face, Standard_False);

        loopEdges.push_back(topoEdge);
        b.builder.Add(loopWire, topoEdge);
    }
    
    Handle(ShapeFix_Wire) wireFixer = 
        new ShapeFix_Wire(loopWire, face, b.ctx->minimumGap);
    if(wireFixer->FixReorder())
        loopWire = wireFixer->Wire();

    loopWire.Closed(true);

    BRepCheck_Analyzer analyser(loopWire, Standard_True);
    if (!analyser.IsValid())
    {
        ShapeFix_Wire sfw(loopWire, face, b.ctx->minimumGap);
        if (sfw.Perform())
        {
            loopWire = sfw.Wire();
            loopWire.Checked(true);
        }
    }
    else
    {
        loopWire.Checked(true);
    }

    /* Apply bound orientation */
    if (!boundData.orientation)
        loopWire.Reverse();

    if (boundData.isOuter)
        outerLoop = loopWire;
    else
        innerLoops.push_back(loopWire);
}


/* ──────────────────────────── BuildFace ──────────────────────────── */

TopoDS_Face xbim_brep_build_face(
    XbimAdvancedBrepBuilder_& b,
    XbimBrepFaceData& faceData)
{
    if (faceData.surface.IsNull())
    {
        xbim_log_warning(b.ctx, "Face has no surface, skipping");
        return TopoDS_Face();
    }

    /* Create a base face from the surface */
    BRepBuilderAPI_MakeFace baseFaceMaker(faceData.surface, b.ctx->precision);
    if (!baseFaceMaker.IsDone())
    {
        xbim_log_warning(b.ctx, "Could not create base face from surface");
        return TopoDS_Face();
    }
    TopoDS_Face baseFace = baseFaceMaker.Face();

    /* Build all wire loops */
    TopoDS_Wire outerLoop;
    std::vector<TopoDS_Wire> innerLoops;

    for (const auto& boundData : faceData.bounds)
    {
        xbim_brep_build_loop_wire(b, boundData, baseFace,
            faceData.buildRuledSurface != 0, outerLoop, innerLoops);
    }

    /* If no outer loop was designated, pick the largest area wire */
    if (outerLoop.IsNull() && !innerLoops.empty())
    {
        double maxArea = 0;
        int foundIndex = -1;
        for (int i = 0; i < (int)innerLoops.size(); ++i)
        {
            double area = ShapeAnalysis::ContourArea(innerLoops[i]);
            if (area > maxArea)
            {
                outerLoop = innerLoops[i];
                maxArea = area;
                foundIndex = i;
            }
        }
        if (foundIndex >= 0)
            innerLoops.erase(innerLoops.begin() + foundIndex);
    }

    if (outerLoop.IsNull())
        return baseFace;

    /* Ruled surface rebuild for IIfcSurfaceOfLinearExtrusion faces.
     *
     *  1. If the wire lies within tolerance of the IFC surface, fix edge pcurves
     *     (ShapeFix_Wire::FixEdgeCurves) and validate with BRepCheck_Analyzer.
     *     Only if the face is already valid do we keep the IFC surface.
     *  2. Otherwise fall through to rebuild_ruled_surface which uses
     *     BRepFill::Face for 4-edge loops. */
    Handle(Geom_Surface) faceSurface = faceData.surface;
    if (faceData.buildRuledSurface)
    {
        bool needRebuild = true;
        bool withinTol = within_tolerance(outerLoop, baseFace, b.ctx->minimumGap);

        if (withinTol)
        {
            ShapeFix_Wire wfIfc(outerLoop, baseFace, b.ctx->minimumGap);
            if (wfIfc.FixEdgeCurves())
                outerLoop = wfIfc.Wire();

            BRepBuilderAPI_MakeFace tryMaker(faceSurface, outerLoop, false);
            if (tryMaker.IsDone())
            {
                BRepCheck_Analyzer analyser(tryMaker.Face(), Standard_True);
                if (analyser.IsValid())
                    needRebuild = false;
            }
        }

        if (needRebuild)
        {
            TopoDS_Face rebuiltFace;
            if (rebuild_ruled_surface(b, outerLoop, rebuiltFace))
            {
                faceSurface = BRep_Tool::Surface(rebuiltFace);
                if (faceSurface.IsNull())
                    faceSurface = faceData.surface;
            }
            else
            {
                xbim_log_warning(b.ctx,
                    "Ruled surface rebuild failed, using original IFC surface");
            }
        }
    }

    /* Build the face from the (possibly rebuilt) surface and outer wire. */
    BRepBuilderAPI_MakeFace faceMaker(faceSurface, outerLoop, false);
    if (!faceMaker.IsDone())
    {
        xbim_log_warning(b.ctx, "Could not create face from surface and outer wire");
        return baseFace;
    }
    TopoDS_Face topoAdvancedFace = faceMaker.Face();

    /* Add inner wires (holes) */
    if (!innerLoops.empty())
    {
        try
        {
            for (auto& innerWire : innerLoops)
            {
                faceMaker.Add(innerWire);
                if (!faceMaker.IsDone())
                {
                    xbim_log_warning(b.ctx,
                        "Could not apply inner bound to face, it has been ignored");
                }
            }

            topoAdvancedFace = faceMaker.Face();

            /* Use ShapeFix_Face::FixOrientation to correctly orient inner
               loops relative to the face (matching old V5 approach — handles
               non-planar surfaces correctly, unlike area-sign heuristics). */
            ShapeFix_Face faceFixer(topoAdvancedFace);
            faceFixer.SetPrecision(b.ctx->precision);
            if (faceFixer.FixOrientation())
                topoAdvancedFace = faceFixer.Face();
        }
        catch (const Standard_Failure& sf)
        {
            xbim_log_warning(b.ctx, "Could not apply inner bound to face: %s",
                sf.GetMessageString() ? sf.GetMessageString() : "unknown");
        }
    }

    try
    {
        BRepCheck_Analyzer analyser(topoAdvancedFace, Standard_False);
        if (!analyser.IsValid())
        {
            ShapeFix_Shape sfs(topoAdvancedFace);
            if (sfs.Perform())
            {
                topoAdvancedFace = TopoDS::Face(sfs.Shape());
                topoAdvancedFace.Checked(true);
            }
        }
        else
        {
            topoAdvancedFace.Checked(true);
        }
    }
    catch (const Standard_Failure& sf)
    {
        xbim_log_warning(b.ctx, "Fixing face failed: %s",
            sf.GetMessageString() ? sf.GetMessageString() : "unknown");
    }

    /* Apply sameSense */
    if (!faceData.sameSense)
        topoAdvancedFace = TopoDS::Face(topoAdvancedFace.Reversed());

    return topoAdvancedFace;
}


/* ──────────────────────────── BuildShell ──────────────────────────── */

TopoDS_Shape xbim_brep_build_shell(
    XbimAdvancedBrepBuilder_& b)
{
    TopoDS_Shell shell;
    b.builder.MakeShell(shell);

    try
    {
        for (auto& faceData : b.faces)
        {
            TopoDS_Face face = xbim_brep_build_face(b, faceData);
            if (!face.IsNull())
                b.builder.Add(shell, face);
        }

        /* Check shell orientation */
        BRepCheck_Shell checker(shell);
        BRepCheck_Status st = checker.Orientation();

        if (st == BRepCheck_NoError)
        {
            shell.Closed(true);
            shell.Checked(true);
            return shell;
        }

        /* Try ShapeFix_Shell */
        ShapeFix_Shell shellFixer(shell);
        shellFixer.SetPrecision(b.ctx->precision);
        if (shellFixer.Perform())
        {
            shell = shellFixer.Shell();
            checker.Init(shell);
        }

        if (checker.Orientation() == BRepCheck_NoError)
        {
            shell.Closed(true);
            shell.Checked(true);
            return shell;
        }

        /* Last resort: ShapeFix_Shape which can produce compound */
        TopoDS_Shape shape = shell;
        ShapeFix_Shape shapeFixer(shape);
        shapeFixer.SetPrecision(b.ctx->minimumGap);
        shapeFixer.SetMinTolerance(b.ctx->minimumGap);
        shapeFixer.SetMaxTolerance(b.ctx->minimumGap * 10);
        if (shapeFixer.Perform())
        {
            shape = shapeFixer.Shape();
        }
        else
        {
            xbim_log_warning(b.ctx, "ShapeFix_Shape could not repair advanced shell");
        }

        return shape;
    }
    catch (const Standard_Failure& exc)
    {
        xbim_log_warning(b.ctx, "Exception building advanced shell: %s",
            exc.GetMessageString() ? exc.GetMessageString() : "unknown");
        return shell;
    }
}


/* ──────────────────────────── C API ──────────────────────────── */

#include "xbim_advanced_brep_builder.h"
#include <BRepClass3d_SolidClassifier.hxx>
#include <TopoDS_Solid.hxx>
#include <TopoDS_Compound.hxx>

XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_create(
    XbimContextHandle ctx,
    XbimAdvancedBrepBuilderHandle* outBuilder)
{
    xbim_clear_error();

    if (!outBuilder)
    {
        xbim_set_error("xbim_advanced_brep_create: outBuilder is NULL");
        return XBIM_INVALID_ARG;
    }
    *outBuilder = nullptr;

    auto* builder = new (std::nothrow) XbimAdvancedBrepBuilder_();
    if (!builder)
    {
        xbim_set_error("xbim_advanced_brep_create: memory allocation failed");
        return XBIM_ERROR;
    }

    builder->ctx = ctx;
    *outBuilder = builder;
    return XBIM_OK;
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_destroy(
    XbimAdvancedBrepBuilderHandle builder)
{
    if (builder)
        delete builder;
    return XBIM_OK;
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_add_vertex(
    XbimAdvancedBrepBuilderHandle builder,
    int vertexLabel,
    double x, double y, double z)
{
    xbim_clear_error();

    if (!builder)
    {
        xbim_set_error("xbim_advanced_brep_add_vertex: builder is NULL");
        return XBIM_INVALID_HANDLE;
    }

    xbim_brep_get_vertex(*builder, vertexLabel, x, y, z);
    return XBIM_OK;
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_add_edge_curve(
    XbimAdvancedBrepBuilderHandle builder,
    int edgeLabel,
    XbimCurveHandle curveHandle)
{
    xbim_clear_error();

    if (!builder)
    {
        xbim_set_error("xbim_advanced_brep_add_edge_curve: builder is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (!curveHandle || curveHandle->curve.IsNull())
    {
        xbim_set_error("xbim_advanced_brep_add_edge_curve: curveHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    builder->curveMap.Bind(edgeLabel, curveHandle->curve);
    return XBIM_OK;
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_begin_face(
    XbimAdvancedBrepBuilderHandle builder,
    XbimSurfaceHandle surfaceHandle,
    int sameSense,
    int buildRuledSurface)
{
    xbim_clear_error();

    if (!builder)
    {
        xbim_set_error("xbim_advanced_brep_begin_face: builder is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (!surfaceHandle || surfaceHandle->surface.IsNull())
    {
        xbim_set_error("xbim_advanced_brep_begin_face: surfaceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    builder->faces.emplace_back();
    builder->currentFace = &builder->faces.back();
    builder->currentFace->surface = surfaceHandle->surface;
    builder->currentFace->sameSense = sameSense;
    builder->currentFace->buildRuledSurface = buildRuledSurface;
    builder->currentBound = nullptr;
    return XBIM_OK;
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_begin_bound(
    XbimAdvancedBrepBuilderHandle builder,
    int isOuter,
    int orientation)
{
    xbim_clear_error();

    if (!builder || !builder->currentFace)
    {
        xbim_set_error("xbim_advanced_brep_begin_bound: no active face");
        return XBIM_INVALID_HANDLE;
    }

    builder->currentFace->bounds.emplace_back();
    builder->currentBound = &builder->currentFace->bounds.back();
    builder->currentBound->isOuter = isOuter;
    builder->currentBound->orientation = orientation;
    return XBIM_OK;
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_add_bound_edge(
    XbimAdvancedBrepBuilderHandle builder,
    int edgeLabel,
    int startVertexLabel,
    int endVertexLabel,
    int sameSense)
{
    xbim_clear_error();

    if (!builder || !builder->currentBound)
    {
        xbim_set_error("xbim_advanced_brep_add_bound_edge: no active bound");
        return XBIM_INVALID_HANDLE;
    }

    XbimBrepEdgeData edgeData;
    edgeData.edgeLabel = edgeLabel;
    edgeData.startVertexLabel = startVertexLabel;
    edgeData.endVertexLabel = endVertexLabel;
    edgeData.sameSense = sameSense;
    builder->currentBound->edges.push_back(edgeData);
    return XBIM_OK;
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_end_bound(
    XbimAdvancedBrepBuilderHandle builder)
{
    xbim_clear_error();

    if (!builder)
    {
        xbim_set_error("xbim_advanced_brep_end_bound: builder is NULL");
        return XBIM_INVALID_HANDLE;
    }

    builder->currentBound = nullptr;
    return XBIM_OK;
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_end_face(
    XbimAdvancedBrepBuilderHandle builder)
{
    xbim_clear_error();

    if (!builder)
    {
        xbim_set_error("xbim_advanced_brep_end_face: builder is NULL");
        return XBIM_INVALID_HANDLE;
    }

    builder->currentFace = nullptr;
    builder->currentBound = nullptr;
    return XBIM_OK;
}


/*
 * Helper: wraps a shell as a solid using ShapeFix_Solid::SolidFromShell
 */
static bool shell_to_solid(
    const TopoDS_Shell& shell,
    TopoDS_Solid& outSolid,
    double tolerance)
{
    if (shell.IsNull() || shell.NbChildren() == 0)
        return false;

    ShapeFix_Solid sfs;
    sfs.SetPrecision(tolerance);
    sfs.SetMinTolerance(tolerance);
    sfs.SetMaxTolerance(tolerance * 10);
    TopoDS_Solid solid = sfs.SolidFromShell(shell);

    if (solid.IsNull())
    {
        /* Fallback: bare MakeSolid */
        BRep_Builder b;
        b.MakeSolid(solid);
        b.Add(solid, shell);
    }

    BRepClass3d_SolidClassifier classifier(solid);
    classifier.PerformInfinitePoint(Precision::Confusion());
    if (classifier.State() == TopAbs_IN)
        solid.Reverse();

    outSolid = solid;
    return true;
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_build(
    XbimAdvancedBrepBuilderHandle builder,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_advanced_brep_build: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!builder)
    {
        xbim_set_error("xbim_advanced_brep_build: builder is NULL");
        return XBIM_INVALID_HANDLE;
    }

    struct BuilderCleanup
    {
        XbimAdvancedBrepBuilder_& b;
        ~BuilderCleanup()
        {
            b.faces.clear();
            b.faces.shrink_to_fit();
            b.curveMap.Clear();
            b.vertexCache.Clear();
            b.edgeCache.Clear();
            b.currentFace = nullptr;
            b.currentBound = nullptr;
        }
    } cleanup{*builder};

    try
    {
        TopoDS_Shape result = xbim_brep_build_shell(*builder);
        if (result.IsNull())
        {
            xbim_set_error("xbim_advanced_brep_build: shell building produced null shape");
            return XBIM_NULL_SHAPE;
        }

        /* Wrap shells into solids */
        if (result.ShapeType() == TopAbs_SHELL)
        {
            TopoDS_Solid solid;
            if (shell_to_solid(TopoDS::Shell(result), solid, builder->ctx->minimumGap))
                *outHandle = xbim_shape_create_from(solid);
            else
                *outHandle = xbim_shape_create_from(result);

            return *outHandle ? XBIM_OK : XBIM_ERROR;
        }

        /* Result is compound — extract shells, wrap as solids */
        BRep_Builder b;
        TopoDS_Compound compound;
        b.MakeCompound(compound);
        int solidCount = 0;

        for (TopExp_Explorer shellExp(result, TopAbs_SHELL); shellExp.More(); shellExp.Next())
        {
            TopoDS_Solid solid;
            if (shell_to_solid(TopoDS::Shell(shellExp.Current()), solid, builder->ctx->minimumGap))
            {
                b.Add(compound, solid);
                solidCount++;
            }
        }

        /* Handle loose faces */
        for (TopExp_Explorer faceExp(result, TopAbs_FACE, TopAbs_SHELL);
             faceExp.More(); faceExp.Next())
        {
            TopoDS_Shell looseShell;
            b.MakeShell(looseShell);
            b.Add(looseShell, TopoDS::Face(faceExp.Current()));

            TopoDS_Solid solid;
            b.MakeSolid(solid);
            b.Add(solid, looseShell);
            b.Add(compound, solid);
            solidCount++;
        }

        if (solidCount == 0)
        {
            *outHandle = xbim_shape_create_from(result);
        }
        else if (solidCount == 1)
        {
            TopExp_Explorer solidExp(compound, TopAbs_SOLID);
            if (solidExp.More())
                *outHandle = xbim_shape_create_from(solidExp.Current());
            else
                *outHandle = xbim_shape_create_from(compound);
        }
        else
        {
            *outHandle = xbim_shape_create_from(compound);
        }

        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(builder->ctx, e, "xbim_advanced_brep_build");
        xbim_set_error("xbim_advanced_brep_build: OCCT exception");
        return XBIM_ERROR;
    }
}
