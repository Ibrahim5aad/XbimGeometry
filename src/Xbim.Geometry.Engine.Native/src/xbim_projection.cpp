/*
 * xbim_projection.cpp
 *
 * Implements footprint projection of 3D shapes onto the XY plane using
 * OCCT Hidden Line Removal (HLR) algorithms.
 *
 * The algorithm:
 * 1. Project the shape using HLR (exact or polyhedral) to get visible/outline edges
 * 2. Triangulate edges and convert to 2D line segments
 * 3. Split intersecting segments at intersection points
 * 4. Build a vertex graph and trace outer boundary loops
 * 5. Simplify by removing colinear points
 */

#include "xbim_projection.h"
#include "xbim_shape.h"
#include "xbim_error.h"
#include "xbim_logging.h"
#include "xbim_context.h"

#include <HLRAlgo_Projector.hxx>
#include <HLRBRep_Algo.hxx>
#include <HLRBRep_HLRToShape.hxx>
#include <HLRBRep_PolyAlgo.hxx>
#include <HLRBRep_PolyHLRToShape.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRepMesh_Vertex.hxx>
#include <BRepBndLib.hxx>
#include <BRepTools.hxx>
#include <BRep_Tool.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS.hxx>
#include <Poly_Polygon3D.hxx>
#include <Geom2d_Line.hxx>
#include <Geom2dAPI_InterCurveCurve.hxx>
#include <gp_Dir2d.hxx>
#include <gp_Vec2d.hxx>
#include <Bnd_Box.hxx>
#include <IMeshTools_Parameters.hxx>
#include <BRep_Builder.hxx>
#include <TopoDS_Compound.hxx>

#include <algorithm>
#include <cmath>
#include <sstream>
#include <unordered_set>

/* ────────────────── XbimFootprint methods ────────────────── */

bool XbimFootprint::IsHole(const TColgp_Array1OfPnt2d& loop) const
{
    gp_Pnt2d firstPoint = loop.First();
    for (auto boundIt = Bounds.cbegin(); boundIt != Bounds.cend(); ++boundIt)
    {
        for (auto polyIt = boundIt->cbegin(); polyIt != boundIt->cend(); ++polyIt)
        {
            if (PointInPolygon(firstPoint, *polyIt))
                return true;
        }
    }
    return false;
}

void XbimFootprint::SimplifyBounds()
{
    for (auto boundIt = Bounds.cbegin(); boundIt != Bounds.cend(); ++boundIt)
    {
        for (auto polyIt = boundIt->begin(); polyIt != boundIt->end(); ++polyIt)
        {
            SimplifyPolygon(*polyIt);
        }
    }
}

void XbimFootprint::SimplifyPolygon(const Handle(Poly_Polygon2D)& poly)
{
    int max_point = poly->NbNodes();
    if (max_point < 3) return;

    TColgp_Array1OfPnt2d& nodes = poly->ChangeNodes();
    int currentNode = 2;

    for (int i = 2; i < max_point; i++)
    {
        gp_Pnt2d prevPnt = nodes.Value(i - 1);
        gp_Pnt2d nextPnt = nodes.Value(i + 1);
        gp_Pnt2d currentPnt = nodes.Value(i);
        double angle = std::abs(GetAngle(prevPnt, currentPnt, nextPnt));
        if (std::abs(angle - M_PI) < 1e-5) // colinear, skip
            continue;
        nodes.SetValue(currentNode++, currentPnt);
    }
    nodes.SetValue(currentNode, nodes.Last());
    nodes.Resize(1, currentNode, true);
}

bool XbimFootprint::PointInPolygon(const gp_Pnt2d& P, const Handle(Poly_Polygon2D)& poly)
{
    int max_point = poly->NbNodes();
    double total_angle = GetAngle(poly->Nodes().Last(), P, poly->Nodes().First());

    for (int i = 1; i < max_point; i++)
    {
        total_angle += GetAngle(poly->Nodes().Value(i), P, poly->Nodes().Value(i + 1));
    }

    return (std::abs(total_angle) > 1);
}

double XbimFootprint::GetAngle(const gp_Pnt2d& A, const gp_Pnt2d& B, const gp_Pnt2d& C)
{
    double dot_product, cross_product;
    DotProductAndLength(A, B, C, dot_product, cross_product);
    return std::atan2(cross_product, dot_product);
}

void XbimFootprint::DotProductAndLength(
    const gp_Pnt2d& A, const gp_Pnt2d& B, const gp_Pnt2d& C,
    double& dotProduct, double& crossProductLength)
{
    double BAx = A.X() - B.X();
    double BAy = A.Y() - B.Y();
    double BCx = C.X() - B.X();
    double BCy = C.Y() - B.Y();
    dotProduct = BAx * BCx + BAy * BCy;
    crossProductLength = BAx * BCy - BAy * BCx;
}

/* ────────────────── Projection helper functions ────────────────── */

int xbim_projection_get_or_add_vertex(
    BRepMesh_VertexInspector& inspector,
    VertexCellFilter& cells,
    double x, double y,
    double tolerance,
    bool& isNew)
{
    gp_XY vertex(x, y);
    gp_XY minVertex(x - tolerance, y - tolerance);
    gp_XY maxVertex(x + tolerance, y + tolerance);
    inspector.SetPoint(vertex);
    cells.Inspect(minVertex, maxVertex, inspector);
    int id = inspector.GetCoincidentPoint();
    isNew = (id <= 0);
    if (isNew)
    {
        id = inspector.NbVertices() + 1;
        BRepMesh_Vertex keyedVertex(vertex, id, BRepMesh_DegreeOfFreedom::BRepMesh_OnSurface);
        inspector.Add(keyedVertex);
        cells.Add(id, minVertex, maxVertex);
    }
    return id;
}

bool xbim_projection_update_leftmost(gp_XY& leftMost, double x, double y, double tolerance)
{
    if (x <= leftMost.X())
    {
        double xDiff = std::abs(leftMost.X() - x);
        if (xDiff <= tolerance)
        {
            if (y < leftMost.Y())
            {
                leftMost.SetX(x);
                leftMost.SetY(y);
                return true;
            }
        }
        else
        {
            leftMost.SetX(x);
            leftMost.SetY(y);
            return true;
        }
    }
    return false;
}

void xbim_projection_connected_points(
    int connectedTo,
    const std::map<int, std::set<int>>& arcs,
    std::set<int>& connected)
{
    auto insertResult = connected.insert(connectedTo);
    if (insertResult.second)
    {
        auto segs = arcs.find(connectedTo);
        if (segs != arcs.end())
        {
            for (auto arcIt : segs->second)
            {
                xbim_projection_connected_points(arcIt, arcs, connected);
            }
        }
    }
}

bool xbim_projection_build_segment(
    const gp_XYZ& pointA,
    const gp_XYZ& pointB,
    double tolerance,
    Handle(Geom2d_TrimmedCurve)& segment)
{
    gp_XY a(pointA.X(), pointA.Y());
    gp_XY b(pointB.X(), pointB.Y());
    if (a.IsEqual(b, tolerance)) return false;
    gp_Vec2d dir = b - a;
    Handle(Geom2d_Line) segLine = new Geom2d_Line(a, dir);
    segment = new Geom2d_TrimmedCurve(segLine, 0, dir.Magnitude());
    return true;
}

void xbim_projection_convert_edge_to_segments(
    const TopoDS_Edge& edge,
    TColGeom2d_SequenceOfCurve& segments,
    double tolerance)
{
    try
    {
        TopLoc_Location location;
        Handle(Poly_Polygon3D) polygon3d = BRep_Tool::Polygon3D(edge, location);
        if (polygon3d.IsNull() || polygon3d->NbNodes() == 0) return;

        bool isIdentity = location.IsIdentity();
        gp_Trsf matrix;
        if (!isIdentity) matrix = location.Transformation();

        auto segIt = polygon3d->Nodes().cbegin();
        gp_XYZ pointA = segIt->XYZ();
        if (!isIdentity) matrix.Transforms(pointA);
        gp_XYZ firstPoint = pointA;
        ++segIt;

        for (; segIt != polygon3d->Nodes().cend(); ++segIt)
        {
            gp_XYZ pointB = segIt->XYZ();
            if (!isIdentity) matrix.Transforms(pointB);
            Handle(Geom2d_TrimmedCurve) segment;
            if (xbim_projection_build_segment(pointA, pointB, tolerance, segment))
                segments.Append(segment);
            pointA = pointB;
        }

        if (edge.Closed())
        {
            Handle(Geom2d_TrimmedCurve) segment;
            if (xbim_projection_build_segment(pointA, firstPoint, tolerance, segment))
                segments.Append(segment);
        }
    }
    catch (const Standard_Failure&)
    {
        // silently skip edges that fail conversion
    }
}

void xbim_projection_find_outer_loops(
    BRepMesh_VertexInspector& inspector,
    std::map<int, std::set<int>>& arcs,
    double tolerance,
    XbimFootprint& footprint)
{
    if (arcs.empty()) return;
    const double twoPi = M_PI * 2;

    // Find the leftmost-lowest point
    auto arcIt = arcs.cbegin();
    gp_XY currentPoint = inspector.GetVertex(arcIt->first).Coord();
    int currentPointId = arcIt->first;
    gp_XY leftMostPoint = currentPoint;
    int leftMostPointIndex = currentPointId;

    for (; arcIt != arcs.cend(); ++arcIt)
    {
        gp_XY point = inspector.GetVertex(arcIt->first).Coord();
        if (xbim_projection_update_leftmost(leftMostPoint, point.X(), point.Y(), tolerance))
            leftMostPointIndex = arcIt->first;
    }

    currentPointId = leftMostPointIndex;
    currentPoint = leftMostPoint;
    gp_Dir2d currentDir(0, -1);
    gp_Dir2d previousDir;
    gp_XY previousPoint;
    std::vector<int> outerBound;
    outerBound.push_back(currentPointId);
    int previousPointId = 0;

    std::unordered_set<XbimLoopSegment> outerBoundSegments;

    do
    {
        const std::set<int>& toPoints = arcs[currentPointId];

        if (toPoints.size() == 1)
        {
            int nextId = *(toPoints.cbegin());
            if (nextId == previousPointId)
            {
                if (outerBound.empty())
                    Standard_Failure::Raise("Footprint boundary cannot be determined.");
                currentPointId = previousPointId;
                currentPoint = previousPoint;
                currentDir = previousDir;
                continue;
            }

            previousPointId = currentPointId;
            previousPoint = currentPoint;
            currentPointId = nextId;
            previousDir = currentDir;
            currentPoint = inspector.GetVertex(nextId).Coord();
            currentDir = currentPoint - previousPoint;
            outerBound.push_back(currentPointId);
        }
        else
        {
            double minAngle = twoPi;
            gp_XY nextPoint;
            int nextPointId = 0;

            for (auto pntId : toPoints)
            {
                if (pntId == previousPointId || currentPointId == pntId)
                    continue;

                if (outerBoundSegments.find(XbimLoopSegment(currentPointId, pntId)) != outerBoundSegments.cend())
                    continue;

                gp_XY nextCandidatePoint = inspector.GetVertex(pntId).Coord();
                gp_Vec2d nextDir = nextCandidatePoint - currentPoint;
                double angle = currentDir.Angle(nextDir);
                if (angle <= 0)
                    angle = twoPi + angle;

                if (angle < minAngle)
                {
                    nextPoint = nextCandidatePoint;
                    nextPointId = pntId;
                    minAngle = angle;
                }
            }

            if (nextPointId == 0 || nextPointId == currentPointId)
            {
                Standard_Failure::Raise("Footprint boundary cannot be determined.");
            }
            else
            {
                outerBoundSegments.insert(XbimLoopSegment(currentPointId, nextPointId));
                outerBound.push_back(nextPointId);
                previousPointId = currentPointId;
                previousPoint = currentPoint;
                previousDir = currentDir;
                currentDir = currentPoint - nextPoint;
                currentPoint = nextPoint;
                currentPointId = nextPointId;
            }
        }
    } while (currentPointId != leftMostPointIndex);

    // Remove all connected points from arcs
    std::set<int> connected;
    xbim_projection_connected_points(leftMostPointIndex, arcs, connected);
    for (auto arc : connected)
        arcs.erase(arc);

    // Record the boundary as a closed polygon
    TColgp_Array1OfPnt2d arrayOfPnt2d(1, (int)outerBound.size());
    int i = 1;
    for (auto vertex : outerBound)
    {
        arrayOfPnt2d.SetValue(i++, inspector.GetVertex(vertex).Coord());
    }

    if (!footprint.IsHole(arrayOfPnt2d))
    {
        Handle(Poly_Polygon2D) polygon = new Poly_Polygon2D(arrayOfPnt2d);
        std::vector<Handle(Poly_Polygon2D)> rings;
        rings.push_back(polygon);
        footprint.Bounds.push_back(rings);
    }

    // Recurse for remaining loops
    if (!arcs.empty())
        xbim_projection_find_outer_loops(inspector, arcs, tolerance, footprint);
}

/* ────────────────── Core footprint creation ────────────────── */

static void create_footprint_impl(
    const TopoDS_Shape& shape,
    double linearDeflection,
    double angularDeflection,
    double tolerance,
    XbimFootprint& footprint,
    bool useHlrPolyAlgo,
    const XbimContext_* ctx)
{
    Bnd_Box box;
    BRepBndLib::AddClose(shape, box);
    Standard_Real srXmin, srYmin, srZmin, srXmax, srYmax, srZmax;

    if (box.IsVoid())
    {
        xbim_log_warning(ctx, "Cannot build footprint: shape bounding box is void.");
        return;
    }
    box.Get(srXmin, srYmin, srZmin, srXmax, srYmax, srZmax);

    try
    {
        HLRAlgo_Projector aProjector;
        TopTools_SequenceOfShape visibleEdges;
        TopTools_SequenceOfShape outlineEdges;

        IMeshTools_Parameters meshParams;
        meshParams.Deflection = linearDeflection;
        meshParams.Angle = angularDeflection;

        if (useHlrPolyAlgo)
        {
            Handle(HLRBRep_PolyAlgo) myPolyAlgo = new HLRBRep_PolyAlgo();
            BRepTools::Clean(shape);
            BRepMesh_IncrementalMesh aMesh(shape, linearDeflection, Standard_False, angularDeflection);

            myPolyAlgo->Load(shape);
            myPolyAlgo->Projector(aProjector);
            myPolyAlgo->Update();

            HLRBRep_PolyHLRToShape aBrepHlr2Shape;
            aBrepHlr2Shape.Update(myPolyAlgo);

            for (TopExp_Explorer e(aBrepHlr2Shape.VCompound(), TopAbs_EDGE); e.More(); e.Next())
            {
                BRepMesh_IncrementalMesh edgeMesh(e.Current(), meshParams.Deflection, Standard_False, meshParams.Angle);
                visibleEdges.Append(e.Current());
            }

            for (TopExp_Explorer e(aBrepHlr2Shape.OutLineVCompound(), TopAbs_EDGE); e.More(); e.Next())
            {
                BRepMesh_IncrementalMesh edgeMesh(e.Current(), meshParams.Deflection, Standard_False, meshParams.Angle);
                outlineEdges.Append(e.Current());
            }
        }
        else
        {
            Handle(HLRBRep_Algo) aHlrBrepAlgo = new HLRBRep_Algo();
            aHlrBrepAlgo->Add(shape);
            aHlrBrepAlgo->Projector(aProjector);
            aHlrBrepAlgo->Update();
            aHlrBrepAlgo->Hide();

            HLRBRep_HLRToShape aBrepHlr2Shape(aHlrBrepAlgo);

            for (TopExp_Explorer e(aBrepHlr2Shape.CompoundOfEdges(HLRBRep_TypeOfResultingEdge::HLRBRep_Sharp, true, false), TopAbs_EDGE); e.More(); e.Next())
            {
                BRepMesh_IncrementalMesh edgeMesh(e.Current(), meshParams.Deflection, Standard_False, meshParams.Angle);
                visibleEdges.Append(e.Current());
            }

            for (TopExp_Explorer e(aBrepHlr2Shape.CompoundOfEdges(HLRBRep_TypeOfResultingEdge::HLRBRep_OutLine, true, false), TopAbs_EDGE); e.More(); e.Next())
            {
                BRepMesh_IncrementalMesh edgeMesh(e.Current(), meshParams.Deflection, Standard_False, meshParams.Angle);
                outlineEdges.Append(e.Current());
            }
        }

        // Convert edges to 2D line segments
        TColGeom2d_SequenceOfCurve segments;
        for (int i = 1; i <= outlineEdges.Size(); i++)
        {
            TopoDS_Edge edge = TopoDS::Edge(outlineEdges.Value(i));
            xbim_projection_convert_edge_to_segments(edge, segments, tolerance);
        }
        for (int i = 1; i <= visibleEdges.Size(); i++)
        {
            TopoDS_Edge edge = TopoDS::Edge(visibleEdges.Value(i));
            xbim_projection_convert_edge_to_segments(edge, segments, tolerance);
        }

        // Find and split intersecting segments
        std::map<int, std::vector<double>> segmentSplits;
        for (int i = 1; i <= segments.Size(); i++)
        {
            Handle(Geom2d_TrimmedCurve) segmentA = Handle(Geom2d_TrimmedCurve)::DownCast(segments.Value(i));
            for (int j = i + 1; j <= segments.Size(); j++)
            {
                Handle(Geom2d_TrimmedCurve) segmentB = Handle(Geom2d_TrimmedCurve)::DownCast(segments.Value(j));
                Geom2dAPI_InterCurveCurve curveInter(segmentA, segmentB, tolerance);
                int numIntersections = curveInter.NbPoints();

                for (int p = 1; p <= numIntersections; p++)
                {
                    gp_Pnt2d startA = segmentA->StartPoint();
                    double distA = startA.Distance(curveInter.Point(p));
                    bool contiguousA = (std::abs(distA) < tolerance ||
                        std::abs(segmentA->FirstParameter() + distA - segmentA->LastParameter()) < tolerance);
                    if (!contiguousA)
                    {
                        double u = segmentA->FirstParameter() + distA;
                        auto result = segmentSplits.emplace(i, std::vector<double>());
                        result.first->second.push_back(u);
                    }

                    gp_Pnt2d startB = segmentB->StartPoint();
                    double distB = startB.Distance(curveInter.Point(p));
                    bool contiguousB = (std::abs(distB) < tolerance ||
                        std::abs(segmentB->FirstParameter() + distB - segmentB->LastParameter()) < tolerance);
                    if (!contiguousB)
                    {
                        double u = segmentB->FirstParameter() + distB;
                        auto result = segmentSplits.emplace(j, std::vector<double>());
                        result.first->second.push_back(u);
                    }
                }
            }
        }

        // Apply splits
        for (auto splitIt = segmentSplits.cbegin(); splitIt != segmentSplits.cend(); ++splitIt)
        {
            int segmentIndex = splitIt->first;
            Handle(Geom2d_TrimmedCurve) segment = Handle(Geom2d_TrimmedCurve)::DownCast(segments.Value(segmentIndex));
            auto splitParams = splitIt->second;
            std::sort(splitParams.begin(), splitParams.end());

            double start = segment->FirstParameter();
            auto splitsIt = splitParams.cbegin();
            double end = *splitsIt;
            if (start == end)
                continue;

            Handle(Geom2d_TrimmedCurve) shortenedSegment = new Geom2d_TrimmedCurve(segment->BasisCurve(), start, end);
            segments.SetValue(segmentIndex, shortenedSegment);
            ++splitsIt;

            for (; splitsIt != splitParams.cend(); ++splitsIt)
            {
                start = end;
                end = *splitsIt;
                if (start == end) continue;
                Handle(Geom2d_TrimmedCurve) newSegment = new Geom2d_TrimmedCurve(segment->BasisCurve(), start, end);
                segments.Append(newSegment);
            }

            if (std::abs(end - segment->LastParameter()) > tolerance)
            {
                Handle(Geom2d_TrimmedCurve) lastSegment = new Geom2d_TrimmedCurve(segment->BasisCurve(), end, segment->LastParameter());
                segments.Append(lastSegment);
            }
        }

        if (segments.Size() == 0)
        {
            xbim_log_warning(ctx, "No line segments found to build footprint.");
            Standard_Failure::Raise("No line segments found to build footprint");
        }

        // Build vertex graph
        BRepMesh_VertexInspector anInspector(new NCollection_IncAllocator);
        VertexCellFilter theCells(2, 10 * tolerance, new NCollection_IncAllocator);
        std::map<int, std::set<int>> arcs;
        anInspector.SetTolerance(tolerance);

        for (auto& segIt : segments)
        {
            Handle(Geom2d_TrimmedCurve) seg = Handle(Geom2d_TrimmedCurve)::DownCast(segIt);
            gp_Pnt2d pointA = seg->StartPoint();
            gp_Pnt2d pointB = seg->EndPoint();

            bool isNewA, isNewB;
            int idA = xbim_projection_get_or_add_vertex(anInspector, theCells, pointA.X(), pointA.Y(), tolerance, isNewA);
            int idB = xbim_projection_get_or_add_vertex(anInspector, theCells, pointB.X(), pointB.Y(), tolerance, isNewB);

            if (idA != idB)
            {
                if (isNewA) arcs[idA] = std::set<int>{ idB }; else arcs[idA].insert(idB);
                if (isNewB) arcs[idB] = std::set<int>{ idA }; else arcs[idB].insert(idA);
            }
        }

        // Trace boundary loops
        xbim_projection_find_outer_loops(anInspector, arcs, tolerance, footprint);
        footprint.SimplifyBounds();

        footprint.MinZ = srZmin;
        footprint.MaxZ = srZmax;
        footprint.IsClose = true;
    }
    catch (const Standard_Failure& sf)
    {
        xbim_log_error(ctx, "Footprint failure, falling back to bounding box: %s", sf.GetMessageString());

        // Fallback: return bounding box as footprint
        TColgp_Array1OfPnt2d arrayOfPnt2d(1, 5);
        arrayOfPnt2d.SetValue(1, gp_Pnt2d(srXmin, srYmin));
        arrayOfPnt2d.SetValue(2, gp_Pnt2d(srXmax, srYmin));
        arrayOfPnt2d.SetValue(3, gp_Pnt2d(srXmax, srYmax));
        arrayOfPnt2d.SetValue(4, gp_Pnt2d(srXmin, srYmax));
        arrayOfPnt2d.SetValue(5, gp_Pnt2d(srXmin, srYmin));
        Handle(Poly_Polygon2D) polygon = new Poly_Polygon2D(arrayOfPnt2d);
        std::vector<Handle(Poly_Polygon2D)> rings;
        rings.push_back(polygon);
        footprint.Bounds.push_back(rings);
        footprint.IsClose = false;
        footprint.MinZ = srZmin;
        footprint.MaxZ = srZmax;
    }

    // Clean cached triangulation if polyhedral algorithm was used
    if (useHlrPolyAlgo)
        BRepTools::Clean(shape);
}

/* ────────────────── Serialization ────────────────── */

void xbim_footprint_serialize(const XbimFootprint& footprint, double** outBuffer, int* outLen)
{
    // Calculate buffer size
    int size = 4; // minZ, maxZ, isClose, numBounds
    for (auto& bound : footprint.Bounds)
    {
        size += 1; // numRings
        for (auto& ring : bound)
        {
            size += 1; // numPoints
            size += ring->NbNodes() * 2; // x,y pairs
        }
    }

    double* buffer = new double[size];
    int idx = 0;

    buffer[idx++] = footprint.MinZ;
    buffer[idx++] = footprint.MaxZ;
    buffer[idx++] = footprint.IsClose ? 1.0 : 0.0;
    buffer[idx++] = static_cast<double>(footprint.Bounds.size());

    for (auto& bound : footprint.Bounds)
    {
        buffer[idx++] = static_cast<double>(bound.size());
        for (auto& ring : bound)
        {
            int numNodes = ring->NbNodes();
            buffer[idx++] = static_cast<double>(numNodes);
            for (int i = 1; i <= numNodes; i++)
            {
                const gp_Pnt2d& pt = ring->Nodes().Value(i);
                buffer[idx++] = pt.X();
                buffer[idx++] = pt.Y();
            }
        }
    }

    *outBuffer = buffer;
    *outLen = size;
}

/* ────────────────── Public C API ────────────────── */

extern "C" {

XBIM_EXPORT XbimResult XBIM_CALL xbim_projection_create_footprint(
    XbimContextHandle ctx,
    XbimShapeHandle shapeHandle,
    double linearDeflection,
    double angularDeflection,
    double tolerance,
    int useHlrPolyAlgo,
    double** outBuffer,
    int* outBufferLen)
{
    if (!shapeHandle || !outBuffer || !outBufferLen)
    {
        xbim_set_error("Invalid handle or output pointer.");
        return XBIM_INVALID_HANDLE;
    }

    if (shapeHandle->shape.IsNull())
    {
        xbim_set_error("Shape is null.");
        return XBIM_NULL_SHAPE;
    }

    try
    {
        XbimFootprint footprint;
        create_footprint_impl(
            shapeHandle->shape,
            linearDeflection,
            angularDeflection,
            tolerance,
            footprint,
            useHlrPolyAlgo != 0,
            ctx);

        if (footprint.Bounds.empty())
        {
            xbim_set_error("Footprint generation produced no boundaries.");
            return XBIM_ERROR;
        }

        xbim_footprint_serialize(footprint, outBuffer, outBufferLen);
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_set_error(e.GetMessageString());
        return XBIM_ERROR;
    }
    catch (const std::exception& e)
    {
        xbim_set_error(e.what());
        return XBIM_ERROR;
    }
    catch (...)
    {
        xbim_set_error("Unknown error during footprint creation.");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_projection_get_outline(
    XbimShapeHandle shapeHandle,
    XbimShapeHandle* outCompound)
{
    if (!shapeHandle || !outCompound)
    {
        xbim_set_error("Invalid handle or output pointer.");
        return XBIM_INVALID_HANDLE;
    }

    if (shapeHandle->shape.IsNull())
    {
        xbim_set_error("Shape is null.");
        return XBIM_NULL_SHAPE;
    }

    try
    {
        HLRAlgo_Projector aProjector;
        Handle(HLRBRep_Algo) aHlrBrepAlgo = new HLRBRep_Algo();

        aHlrBrepAlgo->Add(shapeHandle->shape);
        aHlrBrepAlgo->Projector(aProjector);
        aHlrBrepAlgo->Update();
        aHlrBrepAlgo->Hide();

        HLRBRep_HLRToShape aBrepHlr2Shape(aHlrBrepAlgo);

        BRep_Builder b;
        TopoDS_Compound c;
        b.MakeCompound(c);

        for (TopExp_Explorer e(aBrepHlr2Shape.CompoundOfEdges(HLRBRep_TypeOfResultingEdge::HLRBRep_Sharp, true, false), TopAbs_EDGE); e.More(); e.Next())
            b.Add(c, e.Current());
        for (TopExp_Explorer e(aBrepHlr2Shape.CompoundOfEdges(HLRBRep_TypeOfResultingEdge::HLRBRep_OutLine, true, false), TopAbs_EDGE); e.More(); e.Next())
            b.Add(c, e.Current());

        *outCompound = xbim_shape_create_from(c);
        return (*outCompound) ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_set_error(e.GetMessageString());
        return XBIM_ERROR;
    }
    catch (...)
    {
        xbim_set_error("Unknown error during outline creation.");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT void XBIM_CALL xbim_projection_free_buffer(double* buffer)
{
    delete[] buffer;
}

} // extern "C"
