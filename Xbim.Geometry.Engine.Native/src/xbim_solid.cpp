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
 */

#include "xbim_solid.h"
#include "xbim_shape.h"
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
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakeSolid.hxx>
#include <BRepOffsetAPI_ThruSections.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepTools.hxx>
#include <TopExp_Explorer.hxx>
#include <TopTools_ListOfShape.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Vertex.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Wire.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Shell.hxx>
#include <TopoDS_Solid.hxx>
#include <Standard_Failure.hxx>

/* ── Helper: build gp_Ax2 from 9 doubles ────────────────────────────────── */

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

/* ── Block (box) ─────────────────────────────────────────────────────────── */

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

/* ── Sphere ──────────────────────────────────────────────────────────────── */

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

/* ── Right circular cylinder ─────────────────────────────────────────────── */

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

/* ── Right circular cone ─────────────────────────────────────────────────── */

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

/* ── Rectangular pyramid ─────────────────────────────────────────────────── */

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
        double precision = Precision::Confusion();

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

/* ── Extruded area solid (linear sweep / prism) ──────────────────────── */

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

/* ── Extruded area solid tapered (ThruSections loft) ─────────────────── */

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
