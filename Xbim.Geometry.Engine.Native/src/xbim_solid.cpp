/*
 * xbim_solid.cpp
 *
 * Implements CSG solid primitive construction and sweep operations
 * via the flat C API. Ports the NSolidFactory methods from the C++/CLI engine:
 *   - Block (box)
 *   - Sphere
 *   - Right circular cylinder
 *   - Right circular cone
 *   - Rectangular pyramid
 *   - Extruded area solid (linear sweep / prism)
 *   - Extruded area solid tapered (ThruSections loft)
 *   - Revolved area solid (BRepPrimAPI_MakeRevol)
 *   - Revolved area solid tapered (BRepOffsetAPI_MakePipeShell along arc)
 *   - Swept disk solid (BRepOffsetAPI_MakePipeShell with circular profile)
 *   - Fixed reference swept area solid (BRepOffsetAPI_MakePipeShell with ref surface)
 *   - Sectioned spine (BRepOffsetAPI_MakePipeShell with multiple cross-sections)
 */

#include <cmath>
#include <vector>
#include <algorithm>

#include "xbim_solid.h"
#include "xbim_shape.h"
#include "xbim_surface.h"
#include "xbim_location.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <gp_Ax2.hxx>
#include <gp_Pnt.hxx>
#include <gp_Dir.hxx>
#include <gp_Vec.hxx>
#include <gp_Trsf.hxx>
#include <gp_Pln.hxx>
#include <Precision.hxx>
#include <BRep_Builder.hxx>
#include <BRepPrimAPI_MakeBox.hxx>
#include <BRepPrimAPI_MakeSphere.hxx>
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepPrimAPI_MakeCone.hxx>
#include <BRepPrimAPI_MakePrism.hxx>
#include <BRepPrimAPI_MakeRevol.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakeSolid.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepOffsetAPI_ThruSections.hxx>
#include <BRepOffsetAPI_MakePipeShell.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepTools.hxx>
#include <BRepClass3d_SolidClassifier.hxx>
#include <BRepPrim_Builder.hxx>
#include <GC_MakeArcOfCircle.hxx>
#include <Geom_TrimmedCurve.hxx>
#include <gp_Ax1.hxx>
#include <gp_Ax3.hxx>
#include <gp_Circ.hxx>
#include <ShapeFix_ShapeTolerance.hxx>
#include <ShapeFix_Edge.hxx>
#include <ShapeAnalysis.hxx>
#include <ShapeAnalysis_Surface.hxx>
#include <Bnd_Box.hxx>
#include <BRepBndLib.hxx>
#include <BRepAdaptor_CompCurve.hxx>
#include <BRepBuilderAPI_Transform.hxx>
#include <BRepLib_MakeFace.hxx>
#include <BRepTools_WireExplorer.hxx>
#include <TopExp.hxx>
#include <TopExp_Explorer.hxx>
#include <TopTools_ListOfShape.hxx>
#include <TopTools_Array1OfShape.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Vertex.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Wire.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Shell.hxx>
#include <TopoDS_Solid.hxx>
#include <Standard_Failure.hxx>
#include <TopTools_IndexedMapOfShape.hxx>
#include <Geom_Circle.hxx>
#include <Geom_Plane.hxx>
#include <Geom_Surface.hxx>
#include <GeomLProp_SLProps.hxx>

#pragma region Solid Helpers

static gp_Ax2 make_ax2(
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ)
{
    return gp_Ax2(
        gp_Pnt(originX, originY, originZ),
        gp_Dir(zDirX, zDirY, zDirZ),
        gp_Dir(xDirX, xDirY, xDirZ));
}

#pragma endregion

#pragma region CSG Primitives

XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_block(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double xLen,    double yLen,    double zLen,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_solid_build_block: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (xLen <= 0.0 || yLen <= 0.0 || zLen <= 0.0)
    {
        xbim_set_error("xbim_solid_build_block: dimensions must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        gp_Ax2 ax2 = make_ax2(originX, originY, originZ,
                               zDirX, zDirY, zDirZ,
                               xDirX, xDirY, xDirZ);
        BRepPrimAPI_MakeBox boxMaker(ax2, xLen, yLen, zLen);
        TopoDS_Solid solid = boxMaker.Solid();

        if (solid.IsNull())
        {
            xbim_set_error("xbim_solid_build_block: resulting solid is null");
            xbim_log_error(ctx, "Could not build CsgBlock");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(solid);
        if (!*outHandle)
        {
            xbim_set_error("xbim_solid_build_block: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_solid_build_block");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_solid_build_block: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_sphere(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_solid_build_sphere: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (radius <= 0.0)
    {
        xbim_set_error("xbim_solid_build_sphere: radius must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        gp_Ax2 ax2 = make_ax2(originX, originY, originZ,
                               zDirX, zDirY, zDirZ,
                               xDirX, xDirY, xDirZ);
        BRepPrimAPI_MakeSphere sphereMaker(ax2, radius);
        TopoDS_Solid solid = sphereMaker.Solid();

        if (solid.IsNull())
        {
            xbim_set_error("xbim_solid_build_sphere: resulting solid is null");
            xbim_log_error(ctx, "Could not build CsgSphere");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(solid);
        if (!*outHandle)
        {
            xbim_set_error("xbim_solid_build_sphere: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_solid_build_sphere");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_solid_build_sphere: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_right_circular_cylinder(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,  double height,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_solid_build_right_circular_cylinder: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (radius <= 0.0 || height <= 0.0)
    {
        xbim_set_error("xbim_solid_build_right_circular_cylinder: radius and height must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        gp_Ax2 ax2 = make_ax2(originX, originY, originZ,
                               zDirX, zDirY, zDirZ,
                               xDirX, xDirY, xDirZ);
        BRepPrimAPI_MakeCylinder cylinderMaker(ax2, radius, height);
        TopoDS_Solid solid = cylinderMaker.Solid();

        if (solid.IsNull())
        {
            xbim_set_error("xbim_solid_build_right_circular_cylinder: resulting solid is null");
            xbim_log_error(ctx, "Could not build CsgRightCylinder");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(solid);
        if (!*outHandle)
        {
            xbim_set_error("xbim_solid_build_right_circular_cylinder: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_solid_build_right_circular_cylinder");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_solid_build_right_circular_cylinder: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_right_circular_cone(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,  double height,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_solid_build_right_circular_cone: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (radius <= 0.0 || height <= 0.0)
    {
        xbim_set_error("xbim_solid_build_right_circular_cone: radius and height must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        gp_Ax2 ax2 = make_ax2(originX, originY, originZ,
                               zDirX, zDirY, zDirZ,
                               xDirX, xDirY, xDirZ);
        /* Top radius = 0 makes a true cone (apex at top) */
        BRepPrimAPI_MakeCone coneMaker(ax2, radius, 0.0, height);
        TopoDS_Solid solid = coneMaker.Solid();

        if (solid.IsNull())
        {
            xbim_set_error("xbim_solid_build_right_circular_cone: resulting solid is null");
            xbim_log_error(ctx, "Could not build CsgRightCircularCone");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(solid);
        if (!*outHandle)
        {
            xbim_set_error("xbim_solid_build_right_circular_cone: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_solid_build_right_circular_cone");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_solid_build_right_circular_cone: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_rectangular_pyramid(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double xLen,    double yLen,    double height,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_solid_build_rectangular_pyramid: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (xLen <= 0.0 || yLen <= 0.0 || height <= 0.0)
    {
        xbim_set_error("xbim_solid_build_rectangular_pyramid: dimensions must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        /*
         * Build the pyramid manually using BRep_Builder.
         * This matches the NSolidFactory::BuildRectangularPyramid implementation.
         * The pyramid is constructed in local coordinates (base at Z=0), then
         * transformed to the requested axis placement via shape.Moved().
         */
        double xOff = xLen / 2.0;
        double yOff = yLen / 2.0;
        double precision = ctx->precision;

        /* Base rectangle corners + apex */
        gp_Pnt bl(0, 0, 0);
        gp_Pnt br(xLen, 0, 0);
        gp_Pnt tr(xLen, yLen, 0);
        gp_Pnt tl(0, yLen, 0);
        gp_Pnt apex(xOff, yOff, height);

        BRep_Builder builder;

        /* Vertices */
        TopoDS_Vertex vbl, vbr, vtr, vtl, vp;
        builder.MakeVertex(vbl, bl, precision);
        builder.MakeVertex(vbr, br, precision);
        builder.MakeVertex(vtr, tr, precision);
        builder.MakeVertex(vtl, tl, precision);
        builder.MakeVertex(vp, apex, precision);

        /* Base face edges (wound for outward normal pointing down) */
        TopoDS_Edge brbl = BRepBuilderAPI_MakeEdge(vbr, vbl);
        TopoDS_Edge trbr = BRepBuilderAPI_MakeEdge(vtr, vbr);
        TopoDS_Edge tltr = BRepBuilderAPI_MakeEdge(vtl, vtr);
        TopoDS_Edge bltl = BRepBuilderAPI_MakeEdge(vbl, vtl);

        /* Base wire */
        TopoDS_Wire baseWire;
        builder.MakeWire(baseWire);
        builder.Add(baseWire, brbl);
        builder.Add(baseWire, bltl);
        builder.Add(baseWire, tltr);
        builder.Add(baseWire, trbr);

        /* Base face (normal pointing down: Z = -1) */
        BRepBuilderAPI_MakeFace baseFaceMaker(
            gp_Pln(gp_Pnt(0, 0, 0), gp_Dir(0, 0, -1)), baseWire, Standard_True);

        /* Shell */
        TopoDS_Shell shell;
        builder.MakeShell(shell);
        builder.Add(shell, baseFaceMaker.Face());

        /* Side edges from base vertices to apex */
        TopoDS_Edge blp = BRepBuilderAPI_MakeEdge(vbl, vp);
        TopoDS_Edge tlp = BRepBuilderAPI_MakeEdge(vtl, vp);
        TopoDS_Edge brp = BRepBuilderAPI_MakeEdge(vbr, vp);
        TopoDS_Edge trp = BRepBuilderAPI_MakeEdge(vtr, vp);

        /* Side face 1: bl-tl edge -> apex */
        {
            TopoDS_Wire w;
            builder.MakeWire(w);
            builder.Add(w, TopoDS::Edge(bltl.Reversed()));
            builder.Add(w, blp);
            builder.Add(w, TopoDS::Edge(tlp.Reversed()));
            BRepBuilderAPI_MakeFace faceMaker(w, Standard_True);
            builder.Add(shell, faceMaker.Face());
        }

        /* Side face 2: tl-tr edge -> apex */
        {
            TopoDS_Wire w;
            builder.MakeWire(w);
            builder.Add(w, TopoDS::Edge(tltr.Reversed()));
            builder.Add(w, tlp);
            builder.Add(w, TopoDS::Edge(trp.Reversed()));
            BRepBuilderAPI_MakeFace faceMaker(w, Standard_True);
            builder.Add(shell, faceMaker.Face());
        }

        /* Side face 3: tr-br edge -> apex */
        {
            TopoDS_Wire w;
            builder.MakeWire(w);
            builder.Add(w, TopoDS::Edge(trbr.Reversed()));
            builder.Add(w, trp);
            builder.Add(w, TopoDS::Edge(brp.Reversed()));
            BRepBuilderAPI_MakeFace faceMaker(w, Standard_True);
            builder.Add(shell, faceMaker.Face());
        }

        /* Side face 4: br-bl edge -> apex */
        {
            TopoDS_Wire w;
            builder.MakeWire(w);
            builder.Add(w, TopoDS::Edge(brbl.Reversed()));
            builder.Add(w, brp);
            builder.Add(w, TopoDS::Edge(blp.Reversed()));
            BRepBuilderAPI_MakeFace faceMaker(w, Standard_True);
            builder.Add(shell, faceMaker.Face());
        }

        /* Assemble solid from shell */
        BRepBuilderAPI_MakeSolid solidMaker(shell);
        TopoDS_Solid solid = solidMaker.Solid();

        if (solid.IsNull())
        {
            xbim_set_error("xbim_solid_build_rectangular_pyramid: resulting solid is null");
            xbim_log_error(ctx, "Could not build CsgRectangularPyramid");
            return XBIM_NULL_SHAPE;
        }

        /* Apply the axis placement transform */
        gp_Ax2 ax2 = make_ax2(originX, originY, originZ,
                               zDirX, zDirY, zDirZ,
                               xDirX, xDirY, xDirZ);

        /* Build transform from local coords to the target placement */
        gp_Trsf trsf;
        trsf.SetTransformation(gp_Ax3(ax2));
        trsf.Invert();

        TopoDS_Solid movedSolid = TopoDS::Solid(solid.Moved(TopLoc_Location(trsf)));

        *outHandle = xbim_shape_create_from(movedSolid);
        if (!*outHandle)
        {
            xbim_set_error("xbim_solid_build_rectangular_pyramid: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_solid_build_rectangular_pyramid");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_solid_build_rectangular_pyramid: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion

#pragma region Sweep Operations

XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_extruded(
    XbimContextHandle   ctx,
    XbimShapeHandle     faceHandle,
    double dirX, double dirY, double dirZ,
    double depth,
    XbimLocationHandle  locationHandle,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_solid_build_extruded: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!faceHandle)
    {
        xbim_set_error("xbim_solid_build_extruded: faceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    if (depth <= 0.0)
    {
        xbim_set_error("xbim_solid_build_extruded: depth must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        const TopoDS_Shape& faceShape = faceHandle->shape;
        if (faceShape.IsNull())
        {
            xbim_set_error("xbim_solid_build_extruded: face shape is null");
            return XBIM_NULL_SHAPE;
        }

        gp_Vec extrusionVec(gp_Dir(dirX, dirY, dirZ));
        extrusionVec.Multiply(depth);

        BRepPrimAPI_MakePrism prismMaker(faceShape, extrusionVec);
        if (!prismMaker.IsDone())
        {
            xbim_set_error("xbim_solid_build_extruded: prism extrusion failed");
            xbim_log_error(ctx, "Could not build ExtrudedAreaSolid");
            return XBIM_ERROR;
        }

        TopoDS_Shape result = prismMaker.Shape();
        if (result.IsNull())
        {
            xbim_set_error("xbim_solid_build_extruded: resulting shape is null");
            xbim_log_error(ctx, "Could not build ExtrudedAreaSolid");
            return XBIM_NULL_SHAPE;
        }

        /* Apply optional location transform */
        if (locationHandle && !locationHandle->location.IsIdentity())
            result.Move(locationHandle->location);

        *outHandle = xbim_shape_create_from(result);
        if (!*outHandle)
        {
            xbim_set_error("xbim_solid_build_extruded: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_solid_build_extruded");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_solid_build_extruded: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_extruded_tapered(
    XbimContextHandle   ctx,
    XbimShapeHandle     startFaceHandle,
    XbimShapeHandle     endFaceHandle,
    double dirX, double dirY, double dirZ,
    double depth,
    double precision,
    XbimLocationHandle  locationHandle,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_solid_build_extruded_tapered: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!startFaceHandle)
    {
        xbim_set_error("xbim_solid_build_extruded_tapered: startFaceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    if (!endFaceHandle)
    {
        xbim_set_error("xbim_solid_build_extruded_tapered: endFaceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    if (depth <= 0.0)
    {
        xbim_set_error("xbim_solid_build_extruded_tapered: depth must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        const TopoDS_Shape& startShape = startFaceHandle->shape;
        const TopoDS_Shape& endShape = endFaceHandle->shape;

        if (startShape.IsNull() || endShape.IsNull())
        {
            xbim_set_error("xbim_solid_build_extruded_tapered: one or both face shapes are null");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Face startFace = TopoDS::Face(startShape);
        TopoDS_Face endFace = TopoDS::Face(endShape);

        /* Translate the end face to its position along the extrusion direction */
        gp_Vec vec(gp_Dir(dirX, dirY, dirZ));
        vec *= depth;
        gp_Trsf t;
        t.SetTranslation(vec);
        TopoDS_Face placedEndFace = TopoDS::Face(endFace.Moved(TopLoc_Location(t)));

        /* Build the outer body by lofting between start and end outer wires */
        BRepOffsetAPI_ThruSections pipeMaker(Standard_True, Standard_True, precision);
        TopoDS_Wire outerBoundStart = BRepTools::OuterWire(startFace);
        TopoDS_Wire outerBoundEnd = BRepTools::OuterWire(placedEndFace);
        pipeMaker.AddWire(outerBoundStart);
        pipeMaker.AddWire(outerBoundEnd);
        pipeMaker.Build();

        if (!pipeMaker.IsDone())
        {
            xbim_set_error("xbim_solid_build_extruded_tapered: failed to loft outer body");
            xbim_log_error(ctx, "Could not build ExtrudedAreaSolidTapered outer body");
            return XBIM_ERROR;
        }

        TopoDS_Shape taperedOuterBody = pipeMaker.Shape();
        taperedOuterBody.Closed(Standard_True);

        /* Build void solids from inner wires (holes) and cut them from the outer body */
        TopTools_ListOfShape voids;
        TopExp_Explorer startExplorer(startFace, TopAbs_WIRE);
        TopExp_Explorer endExplorer(placedEndFace, TopAbs_WIRE);

        for (; startExplorer.More() && endExplorer.More();
               startExplorer.Next(), endExplorer.Next())
        {
            if (!startExplorer.Current().IsEqual(outerBoundStart))
            {
                BRepOffsetAPI_ThruSections voidPipeMaker(Standard_True, Standard_True, precision);
                voidPipeMaker.AddWire(TopoDS::Wire(startExplorer.Current().Reversed()));
                voidPipeMaker.AddWire(TopoDS::Wire(endExplorer.Current().Reversed()));
                voidPipeMaker.Build();

                if (!voidPipeMaker.IsDone())
                {
                    xbim_log_warning(ctx, "Failed to loft a tapered void — skipping");
                    continue;
                }
                voids.Append(voidPipeMaker.Shape());
            }
        }

        if (voids.Size() > 0)
        {
            for (auto it = voids.cbegin(); it != voids.cend(); ++it)
            {
                BRepAlgoAPI_Cut cutter(taperedOuterBody, *it);
                cutter.Build();
                if (cutter.IsDone())
                {
                    taperedOuterBody = cutter.Shape();
                }
                else
                {
                    xbim_log_warning(ctx, "Boolean cut of tapered void failed — skipping");
                }
            }
        }

        /* Apply optional location transform */
        if (locationHandle && !locationHandle->location.IsIdentity())
            taperedOuterBody.Move(locationHandle->location);

        *outHandle = xbim_shape_create_from(taperedOuterBody);
        if (!*outHandle)
        {
            xbim_set_error("xbim_solid_build_extruded_tapered: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_solid_build_extruded_tapered");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_solid_build_extruded_tapered: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_revolved(
    XbimContextHandle   ctx,
    XbimShapeHandle     faceHandle,
    double axisOriginX, double axisOriginY, double axisOriginZ,
    double axisDirX,    double axisDirY,    double axisDirZ,
    double angle,
    XbimLocationHandle  locationHandle,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_solid_build_revolved: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!faceHandle)
    {
        xbim_set_error("xbim_solid_build_revolved: faceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    if (angle <= 0.0)
    {
        xbim_set_error("xbim_solid_build_revolved: angle must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        const TopoDS_Shape& faceShape = faceHandle->shape;
        if (faceShape.IsNull())
        {
            xbim_set_error("xbim_solid_build_revolved: face shape is null");
            return XBIM_NULL_SHAPE;
        }

        gp_Pnt origin(axisOriginX, axisOriginY, axisOriginZ);
        gp_Dir dir(axisDirX, axisDirY, axisDirZ);
        gp_Ax1 ax1(origin, dir);

        BRepPrimAPI_MakeRevol revol(faceShape, ax1, angle);

        if (!revol.IsDone())
        {
            xbim_set_error("xbim_solid_build_revolved: revolution failed");
            xbim_log_error(ctx, "Could not build RevolvedAreaSolid");
            return XBIM_ERROR;
        }

        TopoDS_Shape result = revol.Shape();
        if (result.IsNull())
        {
            xbim_set_error("xbim_solid_build_revolved: resulting shape is null");
            xbim_log_error(ctx, "Could not build RevolvedAreaSolid");
            return XBIM_NULL_SHAPE;
        }

        /* Apply optional location transform */
        if (locationHandle && !locationHandle->location.IsIdentity())
            result.Move(locationHandle->location);

        /* Apply tolerance fixing */
        ShapeFix_ShapeTolerance tolFixer;
        tolFixer.LimitTolerance(result, ctx->precision);

        *outHandle = xbim_shape_create_from(result);
        if (!*outHandle)
        {
            xbim_set_error("xbim_solid_build_revolved: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_solid_build_revolved");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_solid_build_revolved: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_revolved_tapered(
    XbimContextHandle   ctx,
    XbimShapeHandle     faceHandle,
    XbimShapeHandle     endFaceHandle,
    double axisOriginX, double axisOriginY, double axisOriginZ,
    double axisDirX,    double axisDirY,    double axisDirZ,
    double angle,
    double precision,
    XbimLocationHandle  locationHandle,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_solid_build_revolved_tapered: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!faceHandle)
    {
        xbim_set_error("xbim_solid_build_revolved_tapered: faceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    if (!endFaceHandle)
    {
        xbim_set_error("xbim_solid_build_revolved_tapered: endFaceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    if (angle <= 0.0)
    {
        xbim_set_error("xbim_solid_build_revolved_tapered: angle must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        const TopoDS_Shape& startShape = faceHandle->shape;
        const TopoDS_Shape& endShape = endFaceHandle->shape;

        if (startShape.IsNull() || endShape.IsNull())
        {
            xbim_set_error("xbim_solid_build_revolved_tapered: one or both face shapes are null");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Face startFace = TopoDS::Face(startShape);
        TopoDS_Face endFace = TopoDS::Face(endShape);

        gp_Pnt origin(axisOriginX, axisOriginY, axisOriginZ);
        gp_Dir vz(axisDirX, axisDirY, axisDirZ);

        /* Clamp angle to 2*PI */
        double clampedAngle = std::min(angle, M_PI * 2.0);

        /*
         * Compute the sweep arc: a circular path from the profile centre
         * around the revolution axis.
         */
        /* Get the centroid of the start face bounding box as a proxy for face centre */
        double xMin, yMin, zMin, xMax, yMax, zMax;
        Bnd_Box bbox;
        BRepBndLib::Add(startFace, bbox);
        bbox.Get(xMin, yMin, zMin, xMax, yMax, zMax);
        gp_Pnt faceCentre((xMin + xMax) / 2.0, (yMin + yMax) / 2.0, (zMin + zMax) / 2.0);

        gp_Vec v(origin, faceCentre);
        double radius = v.Magnitude();

        if (radius < ctx->precision)
        {
            xbim_set_error("xbim_solid_build_revolved_tapered: profile centre is on the revolution axis");
            return XBIM_INVALID_ARG;
        }

        gp_Ax2 ax2(origin, vz, gp_Vec(v.X(), v.Y(), v.Z()));
        gp_Circ circ(ax2, radius);
        GC_MakeArcOfCircle arcMaker(circ, 0.0, clampedAngle, Standard_True);
        Handle(Geom_TrimmedCurve) trimmed = arcMaker.Value();

        /* Build a wire from the arc edge for the pipe shell spine */
        TopoDS_Edge arcEdge = BRepBuilderAPI_MakeEdge(trimmed);
        TopoDS_Wire sweepWire = BRepBuilderAPI_MakeWire(arcEdge);

        /*
         * Move the end face to the arc endpoint with correct orientation:
         * normal of profile should be tangent to the arc at the end point.
         */
        gp_Pnt ep;
        gp_Vec tan, norm;
        trimmed->D2(trimmed->LastParameter(), ep, tan, norm);
        gp_Ax3 toAx3(ep, tan, norm);
        gp_Trsf trsf;
        trsf.SetTransformation(toAx3, gp_Ax3());
        endFace.Move(TopLoc_Location(trsf));

        TopoDS_Wire outerBoundStart = BRepTools::OuterWire(startFace);
        TopoDS_Wire outerBoundEnd = BRepTools::OuterWire(endFace);

        /* Collect inner wires from both faces */
        std::vector<TopoDS_Wire> innerWiresStart;
        std::vector<TopoDS_Wire> innerWiresEnd;
        for (TopExp_Explorer ex(startFace, TopAbs_WIRE); ex.More(); ex.Next())
        {
            if (!ex.Current().IsEqual(outerBoundStart))
                innerWiresStart.push_back(TopoDS::Wire(ex.Current()));
        }
        for (TopExp_Explorer ex(endFace, TopAbs_WIRE); ex.More(); ex.Next())
        {
            if (!ex.Current().IsEqual(outerBoundEnd))
                innerWiresEnd.push_back(TopoDS::Wire(ex.Current()));
        }

        /* Build the outer shell via pipe sweep */
        BRepOffsetAPI_MakePipeShell pipeMaker1(sweepWire);
        pipeMaker1.SetTransitionMode(BRepBuilderAPI_Transformed);
        pipeMaker1.Add(outerBoundStart);
        pipeMaker1.Add(outerBoundEnd);
        pipeMaker1.Build();

        if (!pipeMaker1.IsDone())
        {
            xbim_set_error("xbim_solid_build_revolved_tapered: outer pipe shell failed");
            xbim_log_error(ctx, "Could not build RevolvedAreaSolidTapered outer body");
            return XBIM_ERROR;
        }

        /* Assemble the shell from pipe faces + end caps */
        BRepPrim_Builder b;
        TopoDS_Shell shell;
        b.MakeShell(shell);

        TopoDS_Wire firstOuter = TopoDS::Wire(pipeMaker1.FirstShape().Reversed());
        TopoDS_Wire lastOuter = TopoDS::Wire(pipeMaker1.LastShape().Reversed());
        BRepBuilderAPI_MakeFace firstMaker(firstOuter);
        BRepBuilderAPI_MakeFace lastMaker(lastOuter);

        /* Handle inner wires (hollow profiles) */
        int boundsCount = (int)std::min(innerWiresStart.size(), innerWiresEnd.size());
        for (int i = 0; i < boundsCount; i++)
        {
            BRepOffsetAPI_MakePipeShell pipeMaker2(sweepWire);
            pipeMaker2.SetTransitionMode(BRepBuilderAPI_Transformed);
            pipeMaker2.Add(innerWiresStart[i]);
            pipeMaker2.Add(innerWiresEnd[i]);
            pipeMaker2.Build();

            if (pipeMaker2.IsDone())
            {
                for (TopExp_Explorer explr(pipeMaker2.Shape(), TopAbs_FACE); explr.More(); explr.Next())
                {
                    b.AddShellFace(shell, TopoDS::Face(explr.Current().Reversed()));
                }
                firstMaker.Add(TopoDS::Wire(pipeMaker2.FirstShape()));
                lastMaker.Add(TopoDS::Wire(pipeMaker2.LastShape()));
            }
            else
            {
                xbim_log_warning(ctx, "Failed to pipe-sweep a tapered inner wire — skipping");
            }
        }

        /* Add end cap faces and outer pipe faces to the shell */
        b.AddShellFace(shell, firstMaker.Face());
        b.AddShellFace(shell, lastMaker.Face());
        for (TopExp_Explorer explr(pipeMaker1.Shape(), TopAbs_FACE); explr.More(); explr.Next())
        {
            b.AddShellFace(shell, TopoDS::Face(explr.Current()));
        }

        /* Assemble solid from shell */
        TopoDS_Solid solid;
        BRep_Builder bs;
        bs.MakeSolid(solid);
        bs.Add(solid, shell);

        /* Check solid orientation; reverse if inside-out */
        BRepClass3d_SolidClassifier sc(solid);
        sc.PerformInfinitePoint(ctx->precision);
        if (sc.State() == TopAbs_IN)
        {
            bs.MakeSolid(solid);
            shell.Reverse();
            bs.Add(solid, shell);
        }
        solid.Closed(Standard_True);

        /* Apply optional location transform */
        if (locationHandle && !locationHandle->location.IsIdentity())
            solid.Move(locationHandle->location);

        /* Apply tolerance fixing */
        ShapeFix_ShapeTolerance tolFixer;
        tolFixer.LimitTolerance(solid, precision > 0.0 ? precision : ctx->precision);

        *outHandle = xbim_shape_create_from(solid);
        if (!*outHandle)
        {
            xbim_set_error("xbim_solid_build_revolved_tapered: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_solid_build_revolved_tapered");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_solid_build_revolved_tapered: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion

#pragma region Pipe Operations

static Handle(Geom_Curve) get_basis_curve(const Handle(Geom_Curve)& curve)
{
    Handle(Geom_Curve) basis = curve;
    Handle(Geom_TrimmedCurve) trimmed = Handle(Geom_TrimmedCurve)::DownCast(basis);
    while (!trimmed.IsNull())
    {
        basis = trimmed->BasisCurve();
        trimmed = Handle(Geom_TrimmedCurve)::DownCast(basis);
    }
    return basis;
}

/* ── Swept disk solid (BRepOffsetAPI_MakePipeShell with circular profile) */

XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_swept_disk(
    XbimContextHandle   ctx,
    XbimShapeHandle     directrixHandle,
    double              radius,
    double              innerRadius,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_solid_build_swept_disk: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!directrixHandle)
    {
        xbim_set_error("xbim_solid_build_swept_disk: directrixHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    if (radius <= 0.0)
    {
        xbim_set_error("xbim_solid_build_swept_disk: radius must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        const TopoDS_Shape& directrixShape = directrixHandle->shape;
        if (directrixShape.IsNull() || directrixShape.ShapeType() != TopAbs_WIRE)
        {
            xbim_set_error("xbim_solid_build_swept_disk: directrix must be a valid wire");
            return XBIM_INVALID_ARG;
        }

        TopoDS_Wire directrixWire = TopoDS::Wire(directrixShape);

        /* Analyse the directrix wire to determine transition mode and start tangent */
        bool haveFirstEdge = false;
        gp_Vec dirAtStart;
        gp_Pnt startPoint;
        int numPeriodics = 0;

        for (BRepTools_WireExplorer wireExp(directrixWire); wireExp.More(); wireExp.Next())
        {
            double start, end;
            auto edge = wireExp.Current();
            auto curve = BRep_Tool::Curve(edge, start, end);
            auto basisCurve = get_basis_curve(curve);
            if (basisCurve->IsPeriodic())
                numPeriodics++;

            if (!haveFirstEdge)
            {
                haveFirstEdge = true;
                curve->D1(start, startPoint, dirAtStart);
            }
        }

        if (!haveFirstEdge)
        {
            xbim_set_error("xbim_solid_build_swept_disk: directrix wire has no edges");
            return XBIM_INVALID_ARG;
        }

        /* Lines sweep better with RightCorner; curves need Transformed */
        BRepBuilderAPI_TransitionMode transitionMode =
            (numPeriodics > 0) ? BRepBuilderAPI_Transformed : BRepBuilderAPI_RightCorner;

        /* Build the outer circle at the start of the directrix */
        gp_Ax2 axis(startPoint, dirAtStart);

        Handle(Geom_Circle) outerCircle = new Geom_Circle(axis, radius);
        BRepBuilderAPI_MakeEdge outerEdgeMaker(outerCircle);
        BRep_Builder builder;
        TopoDS_Wire outerWire;
        builder.MakeWire(outerWire);
        builder.Add(outerWire, outerEdgeMaker.Edge());

        /* Sweep the outer circle along the directrix */
        BRepOffsetAPI_MakePipeShell outerSweep(directrixWire);
        outerSweep.SetTransitionMode(transitionMode);
        outerSweep.Add(outerWire);
        outerSweep.Build();

        if (!outerSweep.IsDone())
        {
            xbim_set_error("xbim_solid_build_swept_disk: outer pipe shell sweep failed");
            xbim_log_error(ctx, "Could not build SweptDiskSolid outer body");
            return XBIM_ERROR;
        }

        /* Handle inner radius for hollow disk */
        bool hasInner = !std::isnan(innerRadius) && innerRadius > 0.0;

        if (hasInner)
        {
            Handle(Geom_Circle) innerCircle = new Geom_Circle(axis, innerRadius);
            BRepBuilderAPI_MakeEdge innerEdgeMaker(innerCircle);
            BRepBuilderAPI_MakeWire innerWireMaker(innerEdgeMaker.Edge());
            TopoDS_Shape holeWire = innerWireMaker.Wire().Reversed();

            BRepOffsetAPI_MakePipeShell innerSweep(directrixWire);
            innerSweep.SetTransitionMode(transitionMode);
            innerSweep.Add(holeWire);
            innerSweep.Build();

            if (innerSweep.IsDone())
            {
                /* Combine outer and inner shells with end caps */
                BRep_Builder solidBuilder;
                TopoDS_Solid solid;
                TopoDS_Shell shell;
                solidBuilder.MakeSolid(solid);
                solidBuilder.MakeShell(shell);

                /* Add outer faces */
                for (TopExp_Explorer faceEx(outerSweep.Shape(), TopAbs_FACE); faceEx.More(); faceEx.Next())
                    solidBuilder.Add(shell, TopoDS::Face(faceEx.Current()));

                /* Add inner faces */
                for (TopExp_Explorer faceEx(innerSweep.Shape(), TopAbs_FACE); faceEx.More(); faceEx.Next())
                    solidBuilder.Add(shell, TopoDS::Face(faceEx.Current()));

                /* Cap the start and end */
                TopoDS_Face startFace = BRepLib_MakeFace(
                    TopoDS::Wire(outerSweep.FirstShape().Reversed()), Standard_True);
                solidBuilder.Add(startFace, innerSweep.FirstShape().Reversed());

                TopoDS_Face endFace = BRepLib_MakeFace(
                    TopoDS::Wire(outerSweep.LastShape().Reversed()), Standard_True);
                solidBuilder.Add(endFace, innerSweep.LastShape().Reversed());

                solidBuilder.Add(shell, startFace);
                solidBuilder.Add(shell, endFace.Reversed());
                solidBuilder.Add(solid, shell);

                *outHandle = xbim_shape_create_from(solid);
            }
            else
            {
                /* Inner sweep failed; fall back to solid outer sweep */
                xbim_log_warning(ctx, "Could not build inner radius of SweptDiskSolid");
                if (outerSweep.MakeSolid())
                    *outHandle = xbim_shape_create_from(TopoDS::Solid(outerSweep.Shape()));
            }
        }
        else
        {
            /* Simple solid (no inner radius) */
            if (!outerSweep.MakeSolid())
            {
                xbim_set_error("xbim_solid_build_swept_disk: MakeSolid failed");
                return XBIM_ERROR;
            }
            *outHandle = xbim_shape_create_from(TopoDS::Solid(outerSweep.Shape()));
        }

        if (!*outHandle)
        {
            xbim_set_error("xbim_solid_build_swept_disk: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_solid_build_swept_disk");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_solid_build_swept_disk: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── Fixed reference swept area solid (BRepOffsetAPI_MakePipeShell with ref surface) */

XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_fixed_reference_swept(
    XbimContextHandle   ctx,
    XbimShapeHandle     faceHandle,
    XbimShapeHandle     directrixHandle,
    double refSurfaceOriginX, double refSurfaceOriginY, double refSurfaceOriginZ,
    double refSurfaceNormalX, double refSurfaceNormalY, double refSurfaceNormalZ,
    int    isPlanarReferenceSurface,
    double precision,
    XbimLocationHandle  locationHandle,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_solid_build_fixed_reference_swept: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!faceHandle)
    {
        xbim_set_error("xbim_solid_build_fixed_reference_swept: faceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    if (!directrixHandle)
    {
        xbim_set_error("xbim_solid_build_fixed_reference_swept: directrixHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& faceShape = faceHandle->shape;
        const TopoDS_Shape& directrixShape = directrixHandle->shape;

        if (faceShape.IsNull() || faceShape.ShapeType() != TopAbs_FACE)
        {
            xbim_set_error("xbim_solid_build_fixed_reference_swept: faceHandle must be a valid face");
            return XBIM_INVALID_ARG;
        }

        if (directrixShape.IsNull() || directrixShape.ShapeType() != TopAbs_WIRE)
        {
            xbim_set_error("xbim_solid_build_fixed_reference_swept: directrixHandle must be a valid wire");
            return XBIM_INVALID_ARG;
        }

        TopoDS_Face sweptArea = TopoDS::Face(faceShape);
        TopoDS_Wire directrixWire = TopoDS::Wire(directrixShape);
        double prec = (precision > 0.0) ? precision : ctx->precision;

        /* Build the reference surface (a plane through the given origin with given normal) */
        gp_Pnt refOrigin(refSurfaceOriginX, refSurfaceOriginY, refSurfaceOriginZ);
        gp_Dir refNormal(refSurfaceNormalX, refSurfaceNormalY, refSurfaceNormalZ);
        Handle(Geom_Plane) refPlane = new Geom_Plane(refOrigin, refNormal);
        Handle(Geom_Surface) refSurface = refPlane;

        /* Analyse directrix wire continuity */
        BRepAdaptor_CompCurve cc(directrixWire, Standard_True);
        BRepBuilderAPI_TransitionMode transitionMode =
            (cc.Continuity() == GeomAbs_C0) ? BRepBuilderAPI_RightCorner : BRepBuilderAPI_Transformed;

        /* Place the directrix on the reference surface */
        BRepBuilderAPI_MakeFace faceMaker(refSurface, directrixWire);
        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_solid_build_fixed_reference_swept: directrix could not be projected onto reference surface");
            return XBIM_ERROR;
        }

        /* The swept area must be planar */
        Handle(Geom_Plane) sweptAreaPlane = Handle(Geom_Plane)::DownCast(BRep_Tool::Surface(sweptArea));
        if (sweptAreaPlane.IsNull())
        {
            xbim_set_error("xbim_solid_build_fixed_reference_swept: swept area must be a planar face");
            return XBIM_INVALID_ARG;
        }

        gp_Dir sweptAreaZDir = sweptAreaPlane->Axis().Direction();
        gp_Vec sweptAreaXDir = sweptAreaPlane->Position().XDirection();

        /* Get directrix start point and tangent */
        gp_Pnt directrixWireStart;
        gp_Vec tangentAtDirectrixStart;
        cc.D1(cc.FirstParameter(), directrixWireStart, tangentAtDirectrixStart);

        /* Compute reference surface normal at the directrix start */
        ShapeAnalysis_Surface surfaceAnalyser(refSurface);
        gp_Pnt2d uv = surfaceAnalyser.ValueOfUV(directrixWireStart, prec);
        GeomLProp_SLProps props(refSurface, uv.X(), uv.Y(), 1, prec);

        if (!props.IsNormalDefined())
        {
            xbim_set_error("xbim_solid_build_fixed_reference_swept: could not compute reference surface normal at directrix start");
            return XBIM_ERROR;
        }

        gp_Vec normalAtDirectrixStart = props.Normal();
        tangentAtDirectrixStart.Normalize();
        normalAtDirectrixStart.Normalize();

        /* Transform the swept area to the directrix start position */
        gp_Ax3 toAx3(directrixWireStart, tangentAtDirectrixStart, normalAtDirectrixStart);
        gp_Ax3 fromAx3(gp::Origin(), sweptAreaZDir, sweptAreaXDir);
        gp_Trsf sweptAreaTransform;
        sweptAreaTransform.SetTransformation(toAx3, fromAx3);

        TopoDS_Face sweptAreaRepositioned = TopoDS::Face(BRepBuilderAPI_Transform(sweptArea, sweptAreaTransform));
        TopoDS_Wire sweptAreaBound = ShapeAnalysis::OuterWire(sweptAreaRepositioned);

        /* If reference surface is non-planar, add parameter curves to edges */
        TopoDS_Face referenceFace = faceMaker.Face();
        if (!isPlanarReferenceSurface)
        {
            ShapeFix_Edge sfe;
            for (TopExp_Explorer exp(directrixWire, TopAbs_EDGE); exp.More(); exp.Next())
                sfe.FixAddPCurve(TopoDS::Edge(exp.Current()), referenceFace, false, prec);
        }

        /* Build pipe shell sweep */
        BRepOffsetAPI_MakePipeShell pipeMaker(directrixWire);
        pipeMaker.SetTransitionMode(transitionMode);
        pipeMaker.SetMode(referenceFace);

        TopoDS_Edge firstEdge;
        double uOnEdge;
        cc.Edge(cc.FirstParameter(), firstEdge, uOnEdge);
        pipeMaker.Add(sweptAreaBound, TopExp::FirstVertex(firstEdge), Standard_False, Standard_False);

        pipeMaker.Build();
        if (!pipeMaker.IsDone())
        {
            const char* detail = "unknown error";
            if (pipeMaker.ErrorOnSurface())
                detail = "error on surface";
            else
            {
                BRepBuilderAPI_PipeError status = pipeMaker.GetStatus();
                switch (status)
                {
                case BRepBuilderAPI_PipeNotDone:
                    detail = "pipe not done"; break;
                case BRepBuilderAPI_PlaneNotIntersectGuide:
                    detail = "plane not intersect guide"; break;
                case BRepBuilderAPI_ImpossibleContact:
                    detail = "impossible contact"; break;
                default:
                    break;
                }
            }
            xbim_set_error("xbim_solid_build_fixed_reference_swept: could not build swept pipe");
            xbim_log_error(ctx, "FixedReferenceSweptAreaSolid pipe failed: %s", detail);
            return XBIM_ERROR;
        }

        if (!pipeMaker.MakeSolid())
        {
            xbim_set_error("xbim_solid_build_fixed_reference_swept: could not make swept pipe a solid");
            return XBIM_ERROR;
        }

        TopoDS_Solid solid = TopoDS::Solid(pipeMaker.Shape());

        /* Apply optional location transform */
        if (locationHandle && !locationHandle->location.IsIdentity())
            solid.Move(locationHandle->location);

        *outHandle = xbim_shape_create_from(solid);
        if (!*outHandle)
        {
            xbim_set_error("xbim_solid_build_fixed_reference_swept: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_solid_build_fixed_reference_swept");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_solid_build_fixed_reference_swept: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── Surface-curve swept area solid (BRepOffsetAPI_MakePipeShell with arbitrary ref surface) */

XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_surface_curve_swept(
    XbimContextHandle   ctx,
    XbimShapeHandle     faceHandle,
    XbimShapeHandle     directrixHandle,
    XbimSurfaceHandle   surfaceHandle,
    int                 isPlanarReferenceSurface,
    double              precision,
    XbimLocationHandle  locationHandle,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_solid_build_surface_curve_swept: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!faceHandle)
    {
        xbim_set_error("xbim_solid_build_surface_curve_swept: faceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    if (!directrixHandle)
    {
        xbim_set_error("xbim_solid_build_surface_curve_swept: directrixHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    if (!surfaceHandle)
    {
        xbim_set_error("xbim_solid_build_surface_curve_swept: surfaceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& faceShape = faceHandle->shape;
        const TopoDS_Shape& directrixShape = directrixHandle->shape;
        const Handle(Geom_Surface)& refSurface = surfaceHandle->surface;

        if (faceShape.IsNull() || faceShape.ShapeType() != TopAbs_FACE)
        {
            xbim_set_error("xbim_solid_build_surface_curve_swept: faceHandle must be a valid face");
            return XBIM_INVALID_ARG;
        }

        if (directrixShape.IsNull() || directrixShape.ShapeType() != TopAbs_WIRE)
        {
            xbim_set_error("xbim_solid_build_surface_curve_swept: directrixHandle must be a valid wire");
            return XBIM_INVALID_ARG;
        }

        if (refSurface.IsNull())
        {
            xbim_set_error("xbim_solid_build_surface_curve_swept: surfaceHandle contains a null surface");
            return XBIM_INVALID_ARG;
        }

        TopoDS_Face sweptArea = TopoDS::Face(faceShape);
        TopoDS_Wire directrixWire = TopoDS::Wire(directrixShape);
        double prec = (precision > 0.0) ? precision : ctx->precision;

        /* Analyse directrix wire continuity */
        BRepAdaptor_CompCurve cc(directrixWire, Standard_True);
        BRepBuilderAPI_TransitionMode transitionMode =
            (cc.Continuity() == GeomAbs_C0) ? BRepBuilderAPI_RightCorner : BRepBuilderAPI_Transformed;

        /* Place the directrix on the reference surface */
        BRepBuilderAPI_MakeFace faceMaker(refSurface, directrixWire);
        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_solid_build_surface_curve_swept: directrix could not be projected onto reference surface");
            return XBIM_ERROR;
        }

        /* The swept area must be planar */
        Handle(Geom_Plane) sweptAreaPlane = Handle(Geom_Plane)::DownCast(BRep_Tool::Surface(sweptArea));
        if (sweptAreaPlane.IsNull())
        {
            xbim_set_error("xbim_solid_build_surface_curve_swept: swept area must be a planar face");
            return XBIM_INVALID_ARG;
        }

        gp_Dir sweptAreaZDir = sweptAreaPlane->Axis().Direction();
        gp_Vec sweptAreaXDir = sweptAreaPlane->Position().XDirection();

        /* Get directrix start point and tangent */
        gp_Pnt directrixWireStart;
        gp_Vec tangentAtDirectrixStart;
        cc.D1(cc.FirstParameter(), directrixWireStart, tangentAtDirectrixStart);

        /* Compute reference surface normal at the directrix start */
        ShapeAnalysis_Surface surfaceAnalyser(refSurface);
        gp_Pnt2d uv = surfaceAnalyser.ValueOfUV(directrixWireStart, prec);
        GeomLProp_SLProps props(refSurface, uv.X(), uv.Y(), 1, prec);

        if (!props.IsNormalDefined())
        {
            xbim_set_error("xbim_solid_build_surface_curve_swept: could not compute reference surface normal at directrix start");
            return XBIM_ERROR;
        }

        gp_Vec normalAtDirectrixStart = props.Normal();
        tangentAtDirectrixStart.Normalize();
        normalAtDirectrixStart.Normalize();

        /* Transform the swept area to the directrix start position */
        gp_Ax3 toAx3(directrixWireStart, tangentAtDirectrixStart, normalAtDirectrixStart);
        gp_Ax3 fromAx3(gp::Origin(), sweptAreaZDir, sweptAreaXDir);
        gp_Trsf sweptAreaTransform;
        sweptAreaTransform.SetTransformation(toAx3, fromAx3);

        TopoDS_Face sweptAreaRepositioned = TopoDS::Face(BRepBuilderAPI_Transform(sweptArea, sweptAreaTransform));
        TopoDS_Wire sweptAreaBound = ShapeAnalysis::OuterWire(sweptAreaRepositioned);

        /* If reference surface is non-planar, add parameter curves to edges */
        TopoDS_Face referenceFace = faceMaker.Face();
        if (!isPlanarReferenceSurface)
        {
            ShapeFix_Edge sfe;
            for (TopExp_Explorer exp(directrixWire, TopAbs_EDGE); exp.More(); exp.Next())
                sfe.FixAddPCurve(TopoDS::Edge(exp.Current()), referenceFace, false, prec);
        }

        /* Build pipe shell sweep */
        BRepOffsetAPI_MakePipeShell pipeMaker(directrixWire);
        pipeMaker.SetTransitionMode(transitionMode);
        pipeMaker.SetMode(referenceFace);

        TopoDS_Edge firstEdge;
        double uOnEdge;
        cc.Edge(cc.FirstParameter(), firstEdge, uOnEdge);
        pipeMaker.Add(sweptAreaBound, TopExp::FirstVertex(firstEdge), Standard_False, Standard_False);

        pipeMaker.Build();
        if (!pipeMaker.IsDone())
        {
            const char* detail = "unknown error";
            if (pipeMaker.ErrorOnSurface())
                detail = "error on surface";
            else
            {
                BRepBuilderAPI_PipeError status = pipeMaker.GetStatus();
                switch (status)
                {
                case BRepBuilderAPI_PipeNotDone:
                    detail = "pipe not done"; break;
                case BRepBuilderAPI_PlaneNotIntersectGuide:
                    detail = "plane not intersect guide"; break;
                case BRepBuilderAPI_ImpossibleContact:
                    detail = "impossible contact"; break;
                default:
                    break;
                }
            }
            xbim_set_error("xbim_solid_build_surface_curve_swept: could not build swept pipe");
            xbim_log_error(ctx, "SurfaceCurveSweptAreaSolid pipe failed: %s", detail);
            return XBIM_ERROR;
        }

        if (!pipeMaker.MakeSolid())
        {
            xbim_set_error("xbim_solid_build_surface_curve_swept: could not make swept pipe a solid");
            return XBIM_ERROR;
        }

        TopoDS_Solid solid = TopoDS::Solid(pipeMaker.Shape());

        /* Apply optional location transform */
        if (locationHandle && !locationHandle->location.IsIdentity())
            solid.Move(locationHandle->location);

        *outHandle = xbim_shape_create_from(solid);
        if (!*outHandle)
        {
            xbim_set_error("xbim_solid_build_surface_curve_swept: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_solid_build_surface_curve_swept");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_solid_build_surface_curve_swept: OCCT exception");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_sectioned_spine(
    XbimContextHandle        ctx,
    XbimShapeHandle          spineHandle,
    const XbimShapeHandle*   sectionHandles,
    int                      numSections,
    double                   precision,
    XbimShapeHandle*         outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_solid_build_sectioned_spine: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!spineHandle)
    {
        xbim_set_error("xbim_solid_build_sectioned_spine: spineHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    if (!sectionHandles || numSections < 2)
    {
        xbim_set_error("xbim_solid_build_sectioned_spine: need at least 2 section faces");
        return XBIM_INVALID_ARG;
    }

    try
    {
        const TopoDS_Shape& spineShape = spineHandle->shape;
        if (spineShape.IsNull() || spineShape.ShapeType() != TopAbs_WIRE)
        {
            xbim_set_error("xbim_solid_build_sectioned_spine: spineHandle must be a valid wire");
            return XBIM_INVALID_ARG;
        }

        TopoDS_Wire spineWire = TopoDS::Wire(spineShape);

        /* Collect section faces */
        std::vector<TopoDS_Face> sections(numSections);
        for (int i = 0; i < numSections; ++i)
        {
            if (!sectionHandles[i])
            {
                xbim_set_error("xbim_solid_build_sectioned_spine: sectionHandles contains NULL at index");
                return XBIM_INVALID_HANDLE;
            }
            const TopoDS_Shape& s = sectionHandles[i]->shape;
            if (s.IsNull() || s.ShapeType() != TopAbs_FACE)
            {
                xbim_set_error("xbim_solid_build_sectioned_spine: section must be a valid face");
                return XBIM_INVALID_ARG;
            }
            sections[i] = TopoDS::Face(s);
        }

        /* Build outer body: sweep outer wires along the spine */
        BRepOffsetAPI_MakePipeShell outerPipe(spineWire);
        outerPipe.SetTransitionMode(BRepBuilderAPI_Transformed);

        for (int i = 0; i < numSections; ++i)
        {
            TopoDS_Wire outerWire = BRepTools::OuterWire(sections[i]);
            outerPipe.Add(outerWire);
        }

        outerPipe.Build();
        if (!outerPipe.IsDone())
        {
            BRepBuilderAPI_PipeError err = outerPipe.GetStatus();
            xbim_set_error("xbim_solid_build_sectioned_spine: failed to build outer pipe shell");
            xbim_log_error(ctx, "SectionedSpine outer pipe failed with error code %d", (int)err);
            return XBIM_ERROR;
        }

        if (!outerPipe.MakeSolid())
        {
            xbim_set_error("xbim_solid_build_sectioned_spine: could not make outer pipe a solid");
            return XBIM_ERROR;
        }

        TopoDS_Solid outerSolid = TopoDS::Solid(outerPipe.Shape());

        /* Count inner wires (voids) in the first section — assume all sections
           have the same hole topology (same count and ordering). */
        std::vector<TopoDS_Wire> firstInners;
        {
            TopoDS_Wire firstOuter = BRepTools::OuterWire(sections[0]);
            for (TopExp_Explorer exp(sections[0], TopAbs_WIRE); exp.More(); exp.Next())
            {
                TopoDS_Wire w = TopoDS::Wire(exp.Current());
                if (!w.IsSame(firstOuter))
                    firstInners.push_back(w);
            }
        }

        if (!firstInners.empty())
        {
            int numHoles = (int)firstInners.size();

            for (int holeIdx = 0; holeIdx < numHoles; ++holeIdx)
            {
                /* Collect the holeIdx-th inner wire from every section */
                std::vector<TopoDS_Wire> innerWires(numSections);
                bool allFound = true;
                for (int si = 0; si < numSections; ++si)
                {
                    TopoDS_Wire sectionOuter = BRepTools::OuterWire(sections[si]);
                    int innerCount = 0;
                    bool found = false;
                    for (TopExp_Explorer exp(sections[si], TopAbs_WIRE); exp.More(); exp.Next())
                    {
                        TopoDS_Wire w = TopoDS::Wire(exp.Current());
                        if (!w.IsSame(sectionOuter))
                        {
                            if (innerCount == holeIdx)
                            {
                                innerWires[si] = w;
                                found = true;
                                break;
                            }
                            innerCount++;
                        }
                    }
                    if (!found)
                    {
                        xbim_log_warning(ctx, "SectionedSpine: section %d missing inner wire %d — skipping hole", si, holeIdx);
                        allFound = false;
                        break;
                    }
                }

                if (!allFound)
                    continue;

                BRepOffsetAPI_MakePipeShell innerPipe(spineWire);
                innerPipe.SetTransitionMode(BRepBuilderAPI_Transformed);

                for (int si = 0; si < numSections; ++si)
                    innerPipe.Add(innerWires[si]);

                innerPipe.Build();
                if (!innerPipe.IsDone())
                {
                    xbim_log_warning(ctx, "SectionedSpine: failed to build inner pipe for hole %d — skipping", holeIdx);
                    continue;
                }

                if (!innerPipe.MakeSolid())
                {
                    xbim_log_warning(ctx, "SectionedSpine: inner pipe for hole %d could not be made solid — skipping", holeIdx);
                    continue;
                }

                TopoDS_Solid innerSolid = TopoDS::Solid(innerPipe.Shape());

                BRepAlgoAPI_Cut cutter(outerSolid, innerSolid);
                cutter.Build();
                if (cutter.IsDone())
                {
                    outerSolid = TopoDS::Solid(cutter.Shape());
                }
                else
                {
                    xbim_log_warning(ctx, "SectionedSpine: boolean cut for hole %d failed — skipping", holeIdx);
                }
            }
        }

        /* Apply tolerance fix */
        double prec = (precision > 0.0) ? precision : Precision::Confusion();
        ShapeFix_ShapeTolerance tolFixer;
        tolFixer.LimitTolerance(outerSolid, prec);
        outerSolid.Closed(Standard_True);

        *outHandle = xbim_shape_create_from(outerSolid);
        if (!*outHandle)
        {
            xbim_set_error("xbim_solid_build_sectioned_spine: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_solid_build_sectioned_spine");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_solid_build_sectioned_spine: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion
