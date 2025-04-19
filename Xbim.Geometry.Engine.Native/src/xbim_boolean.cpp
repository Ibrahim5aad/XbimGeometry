/*
 * xbim_boolean.cpp
 *
 * Implements boolean operations (union, cut, intersect) via the flat C API.
 * Ports NBooleanFactory from the C++/CLI engine:
 *   - Union (BOPAlgo_FUSE)
 *   - Cut (BOPAlgo_CUT)
 *   - Intersect (BOPAlgo_COMMON)
 *
 * Includes self-intersection detection and automatic shape fixing,
 * topology trimming, and result simplification.
 */

#include "xbim_boolean.h"
#include "xbim_shape.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <sstream>

#include <TopoDS.hxx>
#include <TopoDS_Shape.hxx>
#include <TopoDS_Solid.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Wire.hxx>
#include <TopoDS_Iterator.hxx>
#include <TopAbs_ShapeEnum.hxx>
#include <TopTools_ListOfShape.hxx>
#include <BRepAlgoAPI_BooleanOperation.hxx>
#include <BOPAlgo_PaveFiller.hxx>
#include <BOPAlgo_Alerts.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <ShapeFix_Shape.hxx>
#include <Standard_Failure.hxx>
#include <Standard_Type.hxx>
#include <Precision.hxx>

// Half-space specific includes
#include <gp_Pnt.hxx>
#include <gp_Dir.hxx>
#include <gp_Vec.hxx>
#include <gp_Ax2.hxx>
#include <gp_Ax3.hxx>
#include <gp_Trsf.hxx>
#include <Geom_Plane.hxx>
#include <Geom_CylindricalSurface.hxx>
#include <Geom_SphericalSurface.hxx>
#include <BRep_Builder.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepPrimAPI_MakeHalfSpace.hxx>
#include <BRepPrimAPI_MakePrism.hxx>
#include <TopExp_Explorer.hxx>

/* ── Internal helpers ─────────────────────────────────────────────────────── */

bool is_empty(const TopoDS_Shape& shape)
{
    return shape.IsNull() || shape.NbChildren() == 0;
}

TopoDS_Shape trim_topology(const TopoDS_Shape& shape)
{
    if (shape.ShapeType() != TopAbs_COMPOUND || shape.NbChildren() != 1)
        return shape;
    TopoDS_Iterator it(shape);
    return trim_topology(it.Value());
}

/*
 * Core boolean operation using BRepAlgoAPI_BooleanOperation.
 * Handles:
 *   - Error reporting via OCCT GetReport
 *   - Result validation and SimplifyResult
 *   - Self-intersection detection with automatic shape fixing (one retry)
 */
TopoDS_Shape perform_boolean(
    const XbimContext_* ctx,
    const TopTools_ListOfShape& arguments,
    const TopTools_ListOfShape& tools,
    double fuzzyTolerance,
    BOPAlgo_Operation operation,
    int& hasWarnings,
    bool attemptingFix)
{
    try
    {
        hasWarnings = 0;
        BRepAlgoAPI_BooleanOperation bop;
        bop.SetArguments(arguments);
        bop.SetTools(tools);
        bop.SetOperation(operation);
        bop.SetRunParallel(false);
        bop.SetNonDestructive(true);
        bop.SetFuzzyValue(fuzzyTolerance);
        bop.Build();

        if (bop.HasErrors())
        {
            std::ostringstream msg;
            auto& report = bop.GetReport();
            report->Dump(msg);
            Standard_Failure::Raise(msg.str().c_str());
        }

        if (bop.IsDone())
        {
            TopoDS_Shape result = bop.Shape();
            BRepCheck_Analyzer analyzer(result);
            if (!analyzer.IsValid())
            {
                xbim_log_warning(ctx, "Boolean resulting shape is invalid, skipping SimplifyResult().");
            }
            else
            {
                bop.SimplifyResult(true, true, Precision::Angular());
            }

            // Detect self-intersection acquired during boolean — fix shapes and retry once
            if (bop.DSFiller()->HasWarning(STANDARD_TYPE(BOPAlgo_AlertAcquiredSelfIntersection))
                && !attemptingFix)
            {
                TopTools_ListOfShape fixedArguments;
                TopTools_ListOfShape fixedTools;
                bool fixPossible = false;

                for (auto it = arguments.cbegin(); it != arguments.cend(); ++it)
                {
                    ShapeFix_Shape shapeFixer(*it);
                    if (shapeFixer.Perform())
                    {
                        fixedArguments.Append(shapeFixer.Shape());
                        fixPossible = true;
                    }
                    else
                    {
                        fixedArguments.Append(*it);
                    }
                }
                for (auto it = tools.cbegin(); it != tools.cend(); ++it)
                {
                    ShapeFix_Shape shapeFixer(*it);
                    if (shapeFixer.Perform())
                    {
                        fixedTools.Append(shapeFixer.Shape());
                        fixPossible = true;
                    }
                    else
                    {
                        fixedTools.Append(*it);
                    }
                }

                if (fixPossible)
                    return perform_boolean(ctx, fixedArguments, fixedTools,
                                           fuzzyTolerance, operation, hasWarnings, true);
            }

            if (attemptingFix)
                xbim_log_debug(ctx, "Self-intersection of sub-shapes in Boolean output has been fixed.");

            return trim_topology(bop.Shape());
        }
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "Boolean operation failed");
    }

    xbim_log_error(ctx, "Failed to perform boolean operation");
    return TopoDS_Shape(); // empty shape signals failure
}

/* ── Exported C API ───────────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_boolean_union(
    XbimContextHandle   ctx,
    XbimShapeHandle     bodyHandle,
    XbimShapeHandle     toolHandle,
    double              fuzzyTolerance,
    int*                outHasWarnings,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();
    if (!outHandle) { xbim_set_error("outHandle is NULL"); return XBIM_INVALID_ARG; }
    *outHandle = nullptr;
    if (!outHasWarnings) { xbim_set_error("outHasWarnings is NULL"); return XBIM_INVALID_ARG; }
    *outHasWarnings = 0;
    if (!bodyHandle) { xbim_set_error("bodyHandle is NULL"); return XBIM_INVALID_HANDLE; }
    if (!toolHandle) { xbim_set_error("toolHandle is NULL"); return XBIM_INVALID_HANDLE; }

    const auto& body = bodyHandle->shape;
    const auto& tool = toolHandle->shape;

    // Handle empty shape edge cases
    if (is_empty(body) && is_empty(tool))
    {
        xbim_log_warning(ctx, "Attempt to Union two empty shapes. Result is an empty shape.");
        *outHasWarnings = 1;
        return XBIM_NULL_SHAPE;
    }
    if (is_empty(tool))
    {
        xbim_log_warning(ctx, "Attempt to Union two shapes, the tool is empty. Result is the body.");
        *outHasWarnings = 1;
        *outHandle = xbim_shape_create_from(body);
        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }
    if (is_empty(body))
    {
        xbim_log_warning(ctx, "Attempt to Union two shapes, the body is empty. Result is the tool.");
        *outHasWarnings = 1;
        *outHandle = xbim_shape_create_from(tool);
        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }

    TopTools_ListOfShape arguments;
    TopTools_ListOfShape tools;
    arguments.Append(body);
    tools.Append(tool);

    int hasWarnings = 0;
    TopoDS_Shape result = perform_boolean(ctx, arguments, tools,
                                           fuzzyTolerance, BOPAlgo_FUSE, hasWarnings, false);
    *outHasWarnings = hasWarnings;

    if (result.IsNull())
    {
        xbim_set_error("Boolean union produced a null shape");
        return XBIM_NULL_SHAPE;
    }

    *outHandle = xbim_shape_create_from(result);
    return *outHandle ? XBIM_OK : XBIM_ERROR;
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_boolean_cut(
    XbimContextHandle   ctx,
    XbimShapeHandle     bodyHandle,
    XbimShapeHandle     toolHandle,
    double              fuzzyTolerance,
    int*                outHasWarnings,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();
    if (!outHandle) { xbim_set_error("outHandle is NULL"); return XBIM_INVALID_ARG; }
    *outHandle = nullptr;
    if (!outHasWarnings) { xbim_set_error("outHasWarnings is NULL"); return XBIM_INVALID_ARG; }
    *outHasWarnings = 0;
    if (!bodyHandle) { xbim_set_error("bodyHandle is NULL"); return XBIM_INVALID_HANDLE; }
    if (!toolHandle) { xbim_set_error("toolHandle is NULL"); return XBIM_INVALID_HANDLE; }

    const auto& body = bodyHandle->shape;
    const auto& tool = toolHandle->shape;

    // Handle empty shape edge cases
    if (is_empty(body) && is_empty(tool))
    {
        xbim_log_warning(ctx, "Attempt to Cut two empty shapes. Result is an empty shape.");
        *outHasWarnings = 1;
        return XBIM_NULL_SHAPE;
    }
    if (is_empty(tool))
    {
        xbim_log_warning(ctx, "Attempt to Cut shapes, the tool is empty. Result is the body.");
        *outHasWarnings = 1;
        *outHandle = xbim_shape_create_from(body);
        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }
    if (is_empty(body))
    {
        xbim_log_warning(ctx, "Attempt to Cut shapes, the body is empty. Result is an empty shape.");
        *outHasWarnings = 1;
        return XBIM_NULL_SHAPE;
    }

    TopTools_ListOfShape arguments;
    TopTools_ListOfShape tools;
    arguments.Append(body);
    tools.Append(tool);

    int hasWarnings = 0;
    TopoDS_Shape result = perform_boolean(ctx, arguments, tools,
                                           fuzzyTolerance, BOPAlgo_CUT, hasWarnings, false);
    *outHasWarnings = hasWarnings;

    if (result.IsNull())
    {
        xbim_set_error("Boolean cut produced a null shape");
        return XBIM_NULL_SHAPE;
    }

    *outHandle = xbim_shape_create_from(result);
    return *outHandle ? XBIM_OK : XBIM_ERROR;
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_boolean_intersect(
    XbimContextHandle   ctx,
    XbimShapeHandle     bodyHandle,
    XbimShapeHandle     toolHandle,
    double              fuzzyTolerance,
    int*                outHasWarnings,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();
    if (!outHandle) { xbim_set_error("outHandle is NULL"); return XBIM_INVALID_ARG; }
    *outHandle = nullptr;
    if (!outHasWarnings) { xbim_set_error("outHasWarnings is NULL"); return XBIM_INVALID_ARG; }
    *outHasWarnings = 0;
    if (!bodyHandle) { xbim_set_error("bodyHandle is NULL"); return XBIM_INVALID_HANDLE; }
    if (!toolHandle) { xbim_set_error("toolHandle is NULL"); return XBIM_INVALID_HANDLE; }

    const auto& body = bodyHandle->shape;
    const auto& tool = toolHandle->shape;

    // For intersection, if either is empty there can be no intersection
    if (is_empty(body) || is_empty(tool))
    {
        xbim_log_warning(ctx, "Attempt to Intersect one or more empty shapes. Result is an empty shape.");
        *outHasWarnings = 1;
        return XBIM_NULL_SHAPE;
    }

    TopTools_ListOfShape arguments;
    TopTools_ListOfShape tools;
    arguments.Append(body);
    tools.Append(tool);

    int hasWarnings = 0;
    TopoDS_Shape result = perform_boolean(ctx, arguments, tools,
                                           fuzzyTolerance, BOPAlgo_COMMON, hasWarnings, false);
    *outHasWarnings = hasWarnings;

    if (result.IsNull())
    {
        xbim_set_error("Boolean intersect produced a null shape");
        return XBIM_NULL_SHAPE;
    }

    *outHandle = xbim_shape_create_from(result);
    return *outHandle ? XBIM_OK : XBIM_ERROR;
}

/* ── Half-space operations ──────────────────────────────────────────────── */

/*
 * Helper: build a face and point-in-material from surface parameters.
 * Returns true on success, false on failure (with error set).
 */
static bool build_halfspace_face_and_point(
    const XbimContext_* ctx,
    XbimSurfaceType surfaceType,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    int    agreementFlag,
    double oneMeter,
    double precision,
    TopoDS_Face& outFace,
    gp_Pnt& outPointInMaterial)
{
    try
    {
        gp_Pnt origin(originX, originY, originZ);
        gp_Dir zDir(zDirX, zDirY, zDirZ);
        gp_Dir xDir(xDirX, xDirY, xDirZ);
        gp_Ax2 ax2(origin, zDir, xDir);

        switch (surfaceType)
        {
        case XBIM_SURFACE_PLANE:
        {
            Handle(Geom_Plane) plane = new Geom_Plane(ax2);
            gp_Vec normalDir = plane->Axis().Direction();
            if (agreementFlag) normalDir.Reverse();
            outPointInMaterial = plane->Location().Translated(normalDir * oneMeter);

            BRep_Builder b;
            b.MakeFace(outFace, plane, precision);
            break;
        }
        case XBIM_SURFACE_CYLINDRICAL:
        {
            if (radius <= 0)
            {
                xbim_set_error("Cylindrical surface radius must be > 0");
                return false;
            }
            gp_Ax3 ax3(ax2);
            Handle(Geom_CylindricalSurface) surface = new Geom_CylindricalSurface(ax3, radius);
            gp_Dir normalDir = surface->Axis().Direction();
            outPointInMaterial = surface->Location();
            if (agreementFlag) // material is outside the cylinder
            {
                normalDir.Reverse();
                gp_Vec displace(normalDir);
                displace *= radius * 2;
                outPointInMaterial = surface->Location().Translated(displace);
            }
            outFace = BRepBuilderAPI_MakeFace(surface, precision);
            break;
        }
        case XBIM_SURFACE_SPHERICAL:
        {
            if (radius <= 0)
            {
                xbim_set_error("Spherical surface radius must be > 0");
                return false;
            }
            gp_Ax3 ax3(ax2);
            Handle(Geom_SphericalSurface) surface = new Geom_SphericalSurface(ax3, radius);
            gp_Dir normalDir = surface->Axis().Direction();
            outPointInMaterial = surface->Location();
            if (agreementFlag)
            {
                normalDir.Reverse();
                gp_Vec displace(normalDir);
                displace *= radius * 2;
                outPointInMaterial = surface->Location().Translated(displace);
            }
            outFace = BRepBuilderAPI_MakeFace(surface, precision);
            break;
        }
        default:
            xbim_set_error("Unsupported surface type for half-space construction");
            return false;
        }
        return true;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "Failed to build half-space surface");
        return false;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_halfspace_build(
    XbimContextHandle ctx,
    XbimSurfaceType   surfaceType,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    int    agreementFlag,
    double oneMeter,
    double precision,
    XbimShapeHandle*  outHandle)
{
    xbim_clear_error();
    if (!outHandle) { xbim_set_error("outHandle is NULL"); return XBIM_INVALID_ARG; }
    *outHandle = nullptr;

    TopoDS_Face face;
    gp_Pnt pointInMaterial;
    if (!build_halfspace_face_and_point(ctx, surfaceType,
            originX, originY, originZ,
            zDirX, zDirY, zDirZ,
            xDirX, xDirY, xDirZ,
            radius, agreementFlag, oneMeter, precision,
            face, pointInMaterial))
    {
        return XBIM_ERROR;
    }

    try
    {
        BRepPrimAPI_MakeHalfSpace hsMaker(face, pointInMaterial);
        if (!hsMaker.IsDone())
        {
            xbim_set_error("BRepPrimAPI_MakeHalfSpace failed");
            return XBIM_ERROR;
        }

        *outHandle = xbim_shape_create_from(hsMaker.Solid());
        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "Failed to build half-space solid");
        xbim_set_error("Failed to build half-space solid");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_halfspace_build_polygonal_bounded(
    XbimContextHandle ctx,
    double surfaceOriginX, double surfaceOriginY, double surfaceOriginZ,
    double surfaceZDirX,   double surfaceZDirY,   double surfaceZDirZ,
    double surfaceXDirX,   double surfaceXDirY,   double surfaceXDirZ,
    int    agreementFlag,
    const double* boundaryPointsX,
    const double* boundaryPointsY,
    int    boundaryPointCount,
    double boundaryOriginX, double boundaryOriginY, double boundaryOriginZ,
    double boundaryZDirX,   double boundaryZDirY,   double boundaryZDirZ,
    double boundaryXDirX,   double boundaryXDirY,   double boundaryXDirZ,
    double oneMeter,
    double precision,
    XbimShapeHandle*  outHandle)
{
    xbim_clear_error();
    if (!outHandle) { xbim_set_error("outHandle is NULL"); return XBIM_INVALID_ARG; }
    *outHandle = nullptr;
    if (!boundaryPointsX || !boundaryPointsY)
    {
        xbim_set_error("Boundary points arrays are NULL");
        return XBIM_INVALID_ARG;
    }
    if (boundaryPointCount < 3)
    {
        xbim_set_error("Boundary point count must be >= 3");
        return XBIM_INVALID_ARG;
    }

    // Step 1: Build the basic half-space from the planar surface
    TopoDS_Face baseFace;
    gp_Pnt pointInMaterial;
    if (!build_halfspace_face_and_point(ctx, XBIM_SURFACE_PLANE,
            surfaceOriginX, surfaceOriginY, surfaceOriginZ,
            surfaceZDirX, surfaceZDirY, surfaceZDirZ,
            surfaceXDirX, surfaceXDirY, surfaceXDirZ,
            0.0, agreementFlag, oneMeter, precision,
            baseFace, pointInMaterial))
    {
        return XBIM_ERROR;
    }

    try
    {
        BRepPrimAPI_MakeHalfSpace hsMaker(baseFace, pointInMaterial);
        if (!hsMaker.IsDone())
        {
            xbim_set_error("BRepPrimAPI_MakeHalfSpace failed for polygonal bounded half-space");
            return XBIM_ERROR;
        }
        TopoDS_Solid halfSpace = hsMaker.Solid();

        // Step 2: Build the boundary polygon wire in 2D (XY plane)
        BRepBuilderAPI_MakeWire wireMaker;
        for (int i = 0; i < boundaryPointCount; i++)
        {
            int next = (i + 1) % boundaryPointCount;
            gp_Pnt p1(boundaryPointsX[i], boundaryPointsY[i], 0.0);
            gp_Pnt p2(boundaryPointsX[next], boundaryPointsY[next], 0.0);
            if (p1.Distance(p2) < precision)
                continue; // skip degenerate edges
            BRepBuilderAPI_MakeEdge edgeMaker(p1, p2);
            if (edgeMaker.IsDone())
                wireMaker.Add(edgeMaker.Edge());
        }

        if (!wireMaker.IsDone())
        {
            xbim_log_warning(ctx, "Polygonal boundary wire could not be built");
            xbim_set_error("Polygonal boundary wire could not be built");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Wire boundaryWire = wireMaker.Wire();

        // Ensure counter-clockwise winding using 2D signed area (shoelace formula)
        double signedArea2 = 0.0;
        for (int i = 0; i < boundaryPointCount; i++)
        {
            int next = (i + 1) % boundaryPointCount;
            signedArea2 += boundaryPointsX[i] * boundaryPointsY[next]
                         - boundaryPointsX[next] * boundaryPointsY[i];
        }
        if (signedArea2 < 0.0) // clockwise → reverse to counter-clockwise
            boundaryWire.Reverse();

        // Make a face from the wire
        BRepBuilderAPI_MakeFace faceMaker(boundaryWire);
        if (!faceMaker.IsDone())
        {
            xbim_set_error("Could not make face from boundary wire");
            return XBIM_NULL_SHAPE;
        }
        TopoDS_Face boundaryFace = faceMaker.Face();

        // Step 3: Extrude the boundary face into a prism along Z
        double shiftDistance = 200.0 * oneMeter;
        BRepPrimAPI_MakePrism prismMaker(boundaryFace, gp_Vec(0, 0, shiftDistance));
        if (!prismMaker.IsDone())
        {
            xbim_set_error("Could not extrude boundary polygon into prism");
            return XBIM_ERROR;
        }
        TopoDS_Shape subtractionBody = prismMaker.Shape();

        // Fix the prism if needed
        ShapeFix_Shape shapeFixer(subtractionBody);
        if (shapeFixer.Perform())
            subtractionBody = shapeFixer.Shape();

        // Center the prism along Z
        gp_Trsf shiftDown;
        shiftDown.SetTranslation(gp_Vec(0, 0, -shiftDistance / 2.0));
        subtractionBody.Move(shiftDown);

        // Move to boundary position
        gp_Pnt bndOrigin(boundaryOriginX, boundaryOriginY, boundaryOriginZ);
        gp_Dir bndZDir(boundaryZDirX, boundaryZDirY, boundaryZDirZ);
        gp_Dir bndXDir(boundaryXDirX, boundaryXDirY, boundaryXDirZ);
        gp_Ax3 fromAx3(gp_Pnt(0, 0, 0), gp_Dir(0, 0, 1), gp_Dir(1, 0, 0));
        gp_Ax3 toAx3(gp_Ax2(bndOrigin, bndZDir, bndXDir));
        gp_Trsf bndTrsf;
        bndTrsf.SetTransformation(fromAx3, toAx3);
        bndTrsf.Invert();
        subtractionBody.Move(bndTrsf);

        // Step 4: Intersect the half-space with the prism
        TopTools_ListOfShape arguments;
        TopTools_ListOfShape tools;
        arguments.Append(halfSpace);
        tools.Append(subtractionBody);

        int hasWarnings = 0;
        TopoDS_Shape result = perform_boolean(ctx, arguments, tools,
                                               precision, BOPAlgo_COMMON, hasWarnings, false);

        if (result.IsNull())
        {
            xbim_log_warning(ctx, "Polygonal bounded half-space intersection produced empty result");
            return XBIM_NULL_SHAPE;
        }

        // Try to extract a solid from the result
        if (result.ShapeType() == TopAbs_SOLID)
        {
            *outHandle = xbim_shape_create_from(result);
        }
        else if (result.ShapeType() == TopAbs_COMPOUND)
        {
            TopExp_Explorer exp(result, TopAbs_SOLID);
            if (exp.More())
                *outHandle = xbim_shape_create_from(exp.Current());
            else
            {
                xbim_log_warning(ctx, "Polygonal bounded half-space intersection produced no solid");
                return XBIM_NULL_SHAPE;
            }
        }
        else
        {
            *outHandle = xbim_shape_create_from(result);
        }

        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "Failed to build polygonal bounded half-space");
        xbim_set_error("Failed to build polygonal bounded half-space");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_halfspace_build_boxed(
    XbimContextHandle ctx,
    XbimSurfaceType   surfaceType,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    int    agreementFlag,
    double oneMeter,
    double precision,
    XbimShapeHandle*  outHandle)
{
    // Per IFC spec and original C++/CLI engine: boxed half-space is treated
    // identically to a normal half-space. The box is only a computational hint.
    return xbim_halfspace_build(ctx, surfaceType,
        originX, originY, originZ,
        zDirX, zDirY, zDirZ,
        xDirX, xDirY, xDirZ,
        radius, agreementFlag, oneMeter, precision, outHandle);
}
