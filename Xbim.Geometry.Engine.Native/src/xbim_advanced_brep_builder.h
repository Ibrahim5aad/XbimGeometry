/*
 * xbim_advanced_brep_builder.h
 *
 */

#ifndef XBIM_ADVANCED_BREP_BUILDER_H
#define XBIM_ADVANCED_BREP_BUILDER_H

#include "xbim_context.h"
#include "xbim_curve.h"
#include "xbim_surface.h"
#include "xbim_shape.h"

#include <vector>
#include <TopoDS_Shape.hxx>
#include <TopoDS_Vertex.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Wire.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Shell.hxx>
#include <Geom_Curve.hxx>
#include <Geom_Surface.hxx>
#include <BRep_Builder.hxx>
#include <TopTools_DataMapOfIntegerShape.hxx>
#include <NCollection_DataMap.hxx>


/* Data for a single edge within a face bound */
struct XbimBrepEdgeData
{
    int edgeLabel;
    int startVertexLabel;
    int endVertexLabel;
    int sameSense;
};

/* Data for a single face bound (wire loop) */
struct XbimBrepBoundData
{
    int isOuter;
    int orientation;
    std::vector<XbimBrepEdgeData> edges;
};

/* Data for a single face */
struct XbimBrepFaceData
{
    Handle(Geom_Surface) surface;
    int sameSense;
    std::vector<XbimBrepBoundData> bounds;
};


/* The builder maintains state for accumulating IFC data,
 * then builds the full BRep topology in one shot. */
struct XbimAdvancedBrepBuilder_
{
    XbimContextHandle ctx;
    double tolerance;
    BRep_Builder builder;

    /* Shared topology caches, keyed by IFC entity label */
    TopTools_DataMapOfIntegerShape vertexCache;
    TopTools_DataMapOfIntegerShape edgeCache;

    /* Edge curve geometry, keyed by IFC entity label */
    NCollection_DataMap<int, Handle(Geom_Curve)> curveMap;

    /* Faces to build */
    std::vector<XbimBrepFaceData> faces;

    /* Transient state for builder pattern */
    XbimBrepFaceData* currentFace;
    XbimBrepBoundData* currentBound;

    XbimAdvancedBrepBuilder_()
        : ctx(nullptr), tolerance(0.0), currentFace(nullptr), currentBound(nullptr)
    {}
};

typedef XbimAdvancedBrepBuilder_* XbimAdvancedBrepBuilderHandle;


/* Get or create a vertex by entity label */
TopoDS_Vertex xbim_brep_get_vertex(
    XbimAdvancedBrepBuilder_& b,
    int label,
    double x, double y, double z);

/* Build or retrieve a shared edge by entity label */
TopoDS_Edge xbim_brep_build_orient_edge(
    XbimAdvancedBrepBuilder_& b,
    int edgeLabel,
    int startVertexLabel,
    int endVertexLabel,
    int sameSense);

/* Build a wire loop from edge data, populating outerLoop or innerLoops */
void xbim_brep_build_loop_wire(
    XbimAdvancedBrepBuilder_& b,
    const XbimBrepBoundData& boundData,
    const TopoDS_Face& face,
    TopoDS_Wire& outerLoop,
    std::vector<TopoDS_Wire>& innerLoops);

/* Build a single face from face data */
TopoDS_Face xbim_brep_build_face(
    XbimAdvancedBrepBuilder_& b,
    XbimBrepFaceData& faceData);

/* Build the shell from all accumulated faces */
TopoDS_Shape xbim_brep_build_shell(
    XbimAdvancedBrepBuilder_& b);


#endif /* XBIM_ADVANCED_BREP_BUILDER_H */
