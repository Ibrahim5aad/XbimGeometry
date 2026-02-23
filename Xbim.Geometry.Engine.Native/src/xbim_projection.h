/*
 * xbim_projection.h
 *
 * Internal header for projection/footprint operations.
 * Uses OCCT HLR (Hidden Line Removal) to project 3D shapes
 * onto the XY plane and trace outer boundary polygons.
 *
 * Not part of the public C API - used by native implementation code only.
 */

#ifndef XBIM_PROJECTION_H
#define XBIM_PROJECTION_H

#include "xbim_geometry_api.h"

#include <TopoDS_Shape.hxx>
#include <TopoDS_Edge.hxx>
#include <Geom2d_TrimmedCurve.hxx>
#include <TColGeom2d_SequenceOfCurve.hxx>
#include <Poly_Polygon2D.hxx>
#include <BRepMesh_VertexInspector.hxx>
#include <NCollection_CellFilter.hxx>
#include <TColgp_Array1OfPnt2d.hxx>

#include <map>
#include <set>
#include <vector>

typedef NCollection_CellFilter<BRepMesh_VertexInspector> VertexCellFilter;

/*
 * Footprint data container.
 * Holds the 2D polygon boundaries from projecting a shape onto Z=0.
 */
struct XbimFootprint
{
    /* List of polygon boundaries. Each boundary has one or more rings (outer + holes). */
    std::vector<std::vector<Handle(Poly_Polygon2D)>> Bounds;
    double MinZ = 0;
    double MaxZ = 0;
    bool IsClose = true;

    bool IsHole(const TColgp_Array1OfPnt2d& loop) const;
    void SimplifyBounds();

private:
    static void SimplifyPolygon(const Handle(Poly_Polygon2D)& polygon);
    static bool PointInPolygon(const gp_Pnt2d& P, const Handle(Poly_Polygon2D)& poly);
    static double GetAngle(const gp_Pnt2d& A, const gp_Pnt2d& B, const gp_Pnt2d& C);
    static void DotProductAndLength(const gp_Pnt2d& A, const gp_Pnt2d& B, const gp_Pnt2d& C,
                                    double& dotProduct, double& crossProductLength);
};

/*
 * Loop segment for boundary tracing.
 * Represents a directed edge between two vertex IDs.
 */
struct XbimLoopSegment
{
    int A;
    int B;
    XbimLoopSegment(int a, int b) : A(a), B(b) {}
    bool operator==(const XbimLoopSegment& other) const { return A == other.A && B == other.B; }
};

namespace std {
    template<> struct hash<XbimLoopSegment>
    {
        size_t operator()(const XbimLoopSegment& k) const
        {
            return static_cast<size_t>(k.A) ^ (static_cast<size_t>(k.B) << 1);
        }
    };
}

/*
 * Internal projection algorithm functions.
 */
void xbim_projection_find_outer_loops(
    BRepMesh_VertexInspector& inspector,
    std::map<int, std::set<int>>& arcs,
    double tolerance,
    XbimFootprint& footprint);

void xbim_projection_connected_points(
    int connectedTo,
    const std::map<int, std::set<int>>& arcs,
    std::set<int>& connected);

void xbim_projection_convert_edge_to_segments(
    const TopoDS_Edge& edge,
    TColGeom2d_SequenceOfCurve& segments,
    double tolerance);

bool xbim_projection_build_segment(
    const gp_XYZ& pointA,
    const gp_XYZ& pointB,
    double tolerance,
    Handle(Geom2d_TrimmedCurve)& segment);

int xbim_projection_get_or_add_vertex(
    BRepMesh_VertexInspector& inspector,
    VertexCellFilter& cells,
    double x, double y,
    double tolerance,
    bool& isNew);

bool xbim_projection_update_leftmost(gp_XY& leftMost, double x, double y, double tolerance);

/*
 * Serialize footprint data into a flat double buffer.
 *
 * Buffer format:
 *   [0] minZ
 *   [1] maxZ
 *   [2] isClose (0.0 or 1.0)
 *   [3] numBounds
 *   For each bound:
 *     [n] numRings
 *     For each ring:
 *       [n] numPoints
 *       numPoints * 2 doubles (x, y pairs)
 *
 * Returns the buffer and its length. Caller must free with xbim_projection_free_buffer.
 */
void xbim_footprint_serialize(const XbimFootprint& footprint, double** outBuffer, int* outLen);

#endif /* XBIM_PROJECTION_H */
