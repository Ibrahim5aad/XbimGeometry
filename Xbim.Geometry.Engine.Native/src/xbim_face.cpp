/*
 * xbim_face.cpp
 *
 * Implements face construction and query functions via the flat C API.
 * Ports NFaceFactory methods from the C++/CLI engine:
 *   - Build face from a surface (no bounds)
 *   - Build planar face from a closed wire
 *   - Build advanced face with outer wire, inner wires, surface, and orientation
 *   - Query face area
 *   - Query face normal at parametric centre
 */

#include "xbim_face.h"
#include "xbim_shape.h"
#include "xbim_surface.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <gp_Pnt.hxx>
#include <gp_Dir.hxx>
#include <gp_Vec.hxx>
#include <gp_Pln.hxx>
#include <gp_Ax2.hxx>
#include <gp_Ax3.hxx>
#include <Geom_Plane.hxx>
#include <Geom_CylindricalSurface.hxx>
#include <Geom_SphericalSurface.hxx>
#include <Geom_ToroidalSurface.hxx>
#include <Geom_BSplineSurface.hxx>
#include <Geom_RectangularTrimmedSurface.hxx>
#include <Geom_SurfaceOfLinearExtrusion.hxx>
#include <Geom_SurfaceOfRevolution.hxx>
#include <Geom_Surface.hxx>
#include <BRep_Tool.hxx>
#include <BRep_Builder.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepGProp_Face.hxx>
#include <BRepGProp.hxx>
#include <GProp_GProps.hxx>
#include <ShapeFix_Wire.hxx>
#include <ShapeFix_Shape.hxx>
#include <ShapeAnalysis_Surface.hxx>
#include <GeomLProp_SLProps.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Wire.hxx>
#include <TopExp_Explorer.hxx>
#include <TopAbs_ShapeEnum.hxx>
#include <Standard_Failure.hxx>

#pragma region Face Helpers

/*
 * For non-planar surfaces, we need to add 2D parametric curves (pcurves) to
 * the wire edges so that OCCT can properly trim the surface. Returns true if
 * the resulting face area is positive (CCW orientation).
 * Ports NFaceFactory::AddParameterisedCurves.
 */
static bool add_parametric_curves(Handle(Geom_Surface)& surface,
                                  const TopoDS_Wire& wire,
                                  double tolerance)
{
    Handle(Geom_Plane) plane = Handle(Geom_Plane)::DownCast(surface);
    BRep_Builder b;
    TopoDS_Face face;
    b.MakeFace(face, surface, tolerance);
    b.Add(face, wire);
    if (plane.IsNull()) // no need to add pcurves to planar surfaces
    {
        Handle(ShapeFix_Wire) sfw = new ShapeFix_Wire;
        sfw->ClearModes();
        sfw->FixAddPCurveMode() = true;
        sfw->FixSameParameterMode() = true;
        sfw->Init(wire, face, tolerance);
        sfw->FixEdgeCurves();
    }
    GProp_GProps gProps;
    BRepGProp::SurfaceProperties(face, gProps, tolerance);
    return gProps.Mass() > 0;
}


static Handle(Geom_Surface) make_surface(
    int surfaceType,
    double originX, double originY, double originZ,
    double zDirX, double zDirY, double zDirZ,
    double xDirX, double xDirY, double xDirZ,
    double radius)
{
    gp_Ax2 ax2(
        gp_Pnt(originX, originY, originZ),
        gp_Dir(zDirX, zDirY, zDirZ),
        gp_Dir(xDirX, xDirY, xDirZ));

    switch (surfaceType)
    {
    case XBIM_SURFACE_PLANE:
        return new Geom_Plane(gp_Ax3(ax2));
    case XBIM_SURFACE_CYLINDRICAL:
        return new Geom_CylindricalSurface(gp_Ax3(ax2), radius);
    case XBIM_SURFACE_SPHERICAL:
        return new Geom_SphericalSurface(gp_Ax3(ax2), radius);
    default:
        return Handle(Geom_Surface)();
    }
}

#pragma endregion

#pragma region Face Construction

XBIM_EXPORT XbimResult XBIM_CALL xbim_face_build_from_surface(
    XbimContextHandle ctx,
    int               surfaceType,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    double tolerance,
    XbimShapeHandle*  outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_face_build_from_surface: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    try
    {
        Handle(Geom_Surface) surface = make_surface(
            surfaceType,
            originX, originY, originZ,
            zDirX, zDirY, zDirZ,
            xDirX, xDirY, xDirZ,
            radius);

        if (surface.IsNull())
        {
            xbim_set_error("xbim_face_build_from_surface: unsupported surface type");
            return XBIM_INVALID_ARG;
        }

        BRepBuilderAPI_MakeFace faceMaker(surface, false);
        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_face_build_from_surface: could not build face from surface");
            xbim_log_error(ctx, "Could not apply surface to face");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Face face = faceMaker.Face();
        if (face.IsNull())
        {
            xbim_set_error("xbim_face_build_from_surface: resulting face is null");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(face);
        if (!*outHandle)
        {
            xbim_set_error("xbim_face_build_from_surface: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_face_build_from_surface");
        xbim_set_error("xbim_face_build_from_surface: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_face_build_from_wire(
    XbimContextHandle ctx,
    XbimShapeHandle   wireHandle,
    XbimShapeHandle*  outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_face_build_from_wire: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!wireHandle)
    {
        xbim_set_error("xbim_face_build_from_wire: wireHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = wireHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_WIRE)
        {
            xbim_set_error("xbim_face_build_from_wire: handle is not a wire");
            return XBIM_INVALID_ARG;
        }

        const TopoDS_Wire& wire = TopoDS::Wire(shape);

        BRepBuilderAPI_MakeFace faceMaker(wire, true);
        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_face_build_from_wire: could not build planar face from wire");
            xbim_log_warning(ctx, "Could not build face from wire");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Face face = faceMaker.Face();
        if (face.IsNull())
        {
            xbim_set_error("xbim_face_build_from_wire: resulting face is null");
            return XBIM_NULL_SHAPE;
        }

        face.Closed(true);
        *outHandle = xbim_shape_create_from(face);
        if (!*outHandle)
        {
            xbim_set_error("xbim_face_build_from_wire: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_face_build_from_wire");
        xbim_set_error("xbim_face_build_from_wire: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_face_build_advanced(
    XbimContextHandle        ctx,
    int                      surfaceType,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    XbimShapeHandle          outerWireHandle,
    const XbimShapeHandle*   innerWireHandles,
    int                      numInnerWires,
    double                   tolerance,
    int                      sameSense,
    XbimShapeHandle*         outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_face_build_advanced: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!outerWireHandle)
    {
        xbim_set_error("xbim_face_build_advanced: outerWireHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        Handle(Geom_Surface) surface = make_surface(
            surfaceType,
            originX, originY, originZ,
            zDirX, zDirY, zDirZ,
            xDirX, xDirY, xDirZ,
            radius);

        if (surface.IsNull())
        {
            xbim_set_error("xbim_face_build_advanced: unsupported surface type");
            return XBIM_INVALID_ARG;
        }

        /* Extract outer wire */
        const TopoDS_Shape& outerShape = outerWireHandle->shape;
        if (outerShape.IsNull())
        {
            xbim_set_error("xbim_face_build_advanced: outer wire shape is null");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Wire outerWire;
        if (outerShape.ShapeType() == TopAbs_WIRE)
            outerWire = TopoDS::Wire(outerShape);
        else if (outerShape.ShapeType() == TopAbs_FACE)
        {
            /* Extract the outer wire from a face */
            TopExp_Explorer ex(outerShape, TopAbs_WIRE);
            if (ex.More())
                outerWire = TopoDS::Wire(ex.Current());
        }

        if (outerWire.IsNull())
        {
            xbim_set_error("xbim_face_build_advanced: could not extract outer wire");
            return XBIM_INVALID_ARG;
        }

        /* Add parametric curves and determine orientation */
        bool outerLoopIsCCW = add_parametric_curves(surface, outerWire, tolerance);

        /* Build face with outer wire in correct orientation */
        BRepBuilderAPI_MakeFace faceMaker(
            surface,
            outerLoopIsCCW ? outerWire : TopoDS::Wire(outerWire.Reversed()),
            false);

        /* Add inner wire loops (holes) */
        if (numInnerWires > 0 && innerWireHandles)
        {
            for (int i = 0; i < numInnerWires; ++i)
            {
                if (!innerWireHandles[i])
                    continue;

                const TopoDS_Shape& innerShape = innerWireHandles[i]->shape;
                if (innerShape.IsNull())
                    continue;

                TopoDS_Wire innerWire;
                if (innerShape.ShapeType() == TopAbs_WIRE)
                    innerWire = TopoDS::Wire(innerShape);
                else if (innerShape.ShapeType() == TopAbs_FACE)
                {
                    TopExp_Explorer ex(innerShape, TopAbs_WIRE);
                    if (ex.More())
                        innerWire = TopoDS::Wire(ex.Current());
                }

                if (innerWire.IsNull())
                    continue;

                bool innerLoopIsCCW = add_parametric_curves(surface, innerWire, tolerance);
                /* Inner wires must be CW (clockwise) for proper hole definition */
                if (innerLoopIsCCW)
                    innerWire.Reverse();

                faceMaker.Add(innerWire);
            }
        }

        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_face_build_advanced: could not apply bounds to face");
            xbim_log_error(ctx, "Face specification error: could not apply bounds");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Face result = faceMaker.Face();
        if (!sameSense)
            result = TopoDS::Face(result.Reversed());

        if (result.IsNull())
        {
            xbim_set_error("xbim_face_build_advanced: resulting face is null");
            return XBIM_NULL_SHAPE;
        }

        result.Closed(true);
        *outHandle = xbim_shape_create_from(result);
        if (!*outHandle)
        {
            xbim_set_error("xbim_face_build_advanced: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_face_build_advanced");
        xbim_set_error("xbim_face_build_advanced: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_face_build_advanced_with_surface(
    XbimContextHandle        ctx,
    XbimSurfaceHandle        surfaceHandle,
    XbimShapeHandle          outerWireHandle,
    const XbimShapeHandle*   innerWireHandles,
    int                      numInnerWires,
    double                   tolerance,
    int                      sameSense,
    XbimShapeHandle*         outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_face_build_advanced_with_surface: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!surfaceHandle)
    {
        xbim_set_error("xbim_face_build_advanced_with_surface: surfaceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    if (!outerWireHandle)
    {
        xbim_set_error("xbim_face_build_advanced_with_surface: outerWireHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        Handle(Geom_Surface) surface = surfaceHandle->surface;
        if (surface.IsNull())
        {
            xbim_set_error("xbim_face_build_advanced_with_surface: surface is null");
            return XBIM_NULL_SHAPE;
        }

        const TopoDS_Shape& outerShape = outerWireHandle->shape;
        if (outerShape.IsNull())
        {
            xbim_set_error("xbim_face_build_advanced_with_surface: outer wire shape is null");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Wire outerWire;
        if (outerShape.ShapeType() == TopAbs_WIRE)
            outerWire = TopoDS::Wire(outerShape);
        else if (outerShape.ShapeType() == TopAbs_FACE)
        {
            TopExp_Explorer ex(outerShape, TopAbs_WIRE);
            if (ex.More())
                outerWire = TopoDS::Wire(ex.Current());
        }

        if (outerWire.IsNull())
        {
            xbim_set_error("xbim_face_build_advanced_with_surface: could not extract outer wire");
            return XBIM_INVALID_ARG;
        }

        bool outerLoopIsCCW = add_parametric_curves(surface, outerWire, tolerance);

        BRepBuilderAPI_MakeFace faceMaker(
            surface,
            outerLoopIsCCW ? outerWire : TopoDS::Wire(outerWire.Reversed()),
            false);

        if (numInnerWires > 0 && innerWireHandles)
        {
            for (int i = 0; i < numInnerWires; ++i)
            {
                if (!innerWireHandles[i])
                    continue;

                const TopoDS_Shape& innerShape = innerWireHandles[i]->shape;
                if (innerShape.IsNull())
                    continue;

                TopoDS_Wire innerWire;
                if (innerShape.ShapeType() == TopAbs_WIRE)
                    innerWire = TopoDS::Wire(innerShape);
                else if (innerShape.ShapeType() == TopAbs_FACE)
                {
                    TopExp_Explorer ex(innerShape, TopAbs_WIRE);
                    if (ex.More())
                        innerWire = TopoDS::Wire(ex.Current());
                }

                if (innerWire.IsNull())
                    continue;

                bool innerLoopIsCCW = add_parametric_curves(surface, innerWire, tolerance);
                if (innerLoopIsCCW)
                    innerWire.Reverse();

                faceMaker.Add(innerWire);
            }
        }

        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_face_build_advanced_with_surface: could not apply bounds to face");
            xbim_log_error(ctx, "Face specification error: could not apply bounds");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Face result = faceMaker.Face();
        if (!sameSense)
            result = TopoDS::Face(result.Reversed());

        if (result.IsNull())
        {
            xbim_set_error("xbim_face_build_advanced_with_surface: resulting face is null");
            return XBIM_NULL_SHAPE;
        }

        result.Closed(true);
        *outHandle = xbim_shape_create_from(result);
        if (!*outHandle)
        {
            xbim_set_error("xbim_face_build_advanced_with_surface: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_face_build_advanced_with_surface");
        xbim_set_error("xbim_face_build_advanced_with_surface: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion

#pragma region Face Queries

XBIM_EXPORT XbimResult XBIM_CALL xbim_face_area(
    XbimShapeHandle faceHandle,
    double*         outArea)
{
    xbim_clear_error();

    if (!outArea)
    {
        xbim_set_error("xbim_face_area: outArea is NULL");
        return XBIM_INVALID_ARG;
    }
    *outArea = 0.0;

    if (!faceHandle)
    {
        xbim_set_error("xbim_face_area: faceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = faceHandle->shape;
        if (shape.IsNull())
        {
            xbim_set_error("xbim_face_area: shape is null");
            return XBIM_NULL_SHAPE;
        }

        GProp_GProps gProps;
        BRepGProp::SurfaceProperties(shape, gProps);
        *outArea = gProps.Mass();
        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_face_area: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_face_normal(
    XbimShapeHandle faceHandle,
    double          u,
    double          v,
    double*         outNormalX,
    double*         outNormalY,
    double*         outNormalZ)
{
    xbim_clear_error();

    if (!outNormalX || !outNormalY || !outNormalZ)
    {
        xbim_set_error("xbim_face_normal: output pointer is NULL");
        return XBIM_INVALID_ARG;
    }
    *outNormalX = 0.0;
    *outNormalY = 0.0;
    *outNormalZ = 0.0;

    if (!faceHandle)
    {
        xbim_set_error("xbim_face_normal: faceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = faceHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_FACE)
        {
            xbim_set_error("xbim_face_normal: handle is not a face");
            return XBIM_INVALID_ARG;
        }

        const TopoDS_Face& face = TopoDS::Face(shape);
        BRepGProp_Face prop(face);

        gp_Pnt centre;
        gp_Vec faceNormal;

        /* If u and v are both NaN, use the parametric centre */
        if (u != u || v != v) // NaN check
        {
            double u1, u2, v1, v2;
            prop.Bounds(u1, u2, v1, v2);
            u = (u1 + u2) / 2.0;
            v = (v1 + v2) / 2.0;
        }

        prop.Normal(u, v, centre, faceNormal);

        *outNormalX = faceNormal.X();
        *outNormalY = faceNormal.Y();
        *outNormalZ = faceNormal.Z();

        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_face_normal: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_face_tolerance(
    XbimShapeHandle faceHandle,
    double*         outTolerance)
{
    xbim_clear_error();
    if (!outTolerance) { xbim_set_error("xbim_face_tolerance: outTolerance is NULL"); return XBIM_INVALID_ARG; }
    *outTolerance = 0.0;
    if (!faceHandle) { xbim_set_error("xbim_face_tolerance: faceHandle is NULL"); return XBIM_INVALID_HANDLE; }

    try
    {
        const TopoDS_Shape& shape = faceHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_FACE)
        {
            xbim_set_error("xbim_face_tolerance: handle is not a face");
            return XBIM_INVALID_ARG;
        }

        *outTolerance = BRep_Tool::Tolerance(TopoDS::Face(shape));
        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_face_tolerance: OCCT exception");
        return XBIM_ERROR;
    }
}


/*
 * Map OCCT Geom_Surface dynamic type to XbimSurfaceType int.
 * Values match Xbim.Geometry.Abstractions.XSurfaceType enum.
 */
static int classify_surface(const Handle(Geom_Surface)& surf)
{
    if (surf->IsKind(STANDARD_TYPE(Geom_Plane)))                   return 8;  // IfcPlane
    if (surf->IsKind(STANDARD_TYPE(Geom_CylindricalSurface)))     return 7;  // IfcCylindricalSurface
    if (surf->IsKind(STANDARD_TYPE(Geom_SphericalSurface)))       return 9;  // IfcSphericalSurface
    if (surf->IsKind(STANDARD_TYPE(Geom_ToroidalSurface)))        return 10; // IfcToroidalSurface
    if (surf->IsKind(STANDARD_TYPE(Geom_SurfaceOfLinearExtrusion))) return 5; // IfcSurfaceOfLinearExtrusion
    if (surf->IsKind(STANDARD_TYPE(Geom_SurfaceOfRevolution)))    return 6;  // IfcSurfaceOfRevolution
    if (surf->IsKind(STANDARD_TYPE(Geom_RectangularTrimmedSurface))) return 4; // IfcRectangularTrimmedSurface
    if (surf->IsKind(STANDARD_TYPE(Geom_BSplineSurface)))         return 0;  // IfcBSplineSurfaceWithKnots
    return 0; // default to BSpline
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_face_get_surface(
    XbimShapeHandle    faceHandle,
    XbimSurfaceHandle* outSurface,
    int*               outSurfaceType)
{
    xbim_clear_error();
    if (!outSurface || !outSurfaceType)
    {
        xbim_set_error("xbim_face_get_surface: outSurface or outSurfaceType is NULL");
        return XBIM_INVALID_ARG;
    }
    *outSurface = nullptr;
    *outSurfaceType = 0;
    if (!faceHandle) { xbim_set_error("xbim_face_get_surface: faceHandle is NULL"); return XBIM_INVALID_HANDLE; }

    try
    {
        const TopoDS_Shape& shape = faceHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_FACE)
        {
            xbim_set_error("xbim_face_get_surface: handle is not a face");
            return XBIM_INVALID_ARG;
        }

        Handle(Geom_Surface) surf = BRep_Tool::Surface(TopoDS::Face(shape));
        if (surf.IsNull())
        {
            xbim_set_error("xbim_face_get_surface: face has no surface");
            return XBIM_NULL_SHAPE;
        }

        *outSurfaceType = classify_surface(surf);
        *outSurface = xbim_surface_create_from(surf);
        return *outSurface ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_face_get_surface: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion

#pragma region Face Modification

XBIM_EXPORT XbimResult XBIM_CALL xbim_face_add_wires(
    XbimShapeHandle     faceHandle,
    XbimShapeHandle*    wireHandles,
    int                 wireCount,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_face_add_wires: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!faceHandle || faceHandle->shape.IsNull())
    {
        xbim_set_error("xbim_face_add_wires: invalid face handle");
        return XBIM_INVALID_HANDLE;
    }
    if (!wireHandles || wireCount <= 0)
    {
        xbim_set_error("xbim_face_add_wires: no wires provided");
        return XBIM_INVALID_ARG;
    }

    try
    {
        // Make a copy so we don't mutate the original
        TopoDS_Face face = TopoDS::Face(faceHandle->shape);
        BRep_Builder builder;

        for (int i = 0; i < wireCount; i++)
        {
            if (!wireHandles[i] || wireHandles[i]->shape.IsNull())
                continue;
            if (wireHandles[i]->shape.ShapeType() != TopAbs_WIRE)
                continue;

            const TopoDS_Wire& wire = TopoDS::Wire(wireHandles[i]->shape);
            builder.Add(face, wire);
        }

        *outHandle = xbim_shape_create_from(face);
        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_face_add_wires: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_face_fix(
    XbimShapeHandle     faceHandle,
    double              tolerance,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_face_fix: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!faceHandle || faceHandle->shape.IsNull())
    {
        xbim_set_error("xbim_face_fix: invalid face handle");
        return XBIM_INVALID_HANDLE;
    }
    if (faceHandle->shape.ShapeType() != TopAbs_FACE)
    {
        xbim_set_error("xbim_face_fix: handle is not a face");
        return XBIM_INVALID_ARG;
    }

    try
    {
        TopoDS_Face face = TopoDS::Face(faceHandle->shape);
        ShapeFix_Shape fixer(face);
        fixer.SetPrecision(tolerance);
        bool ok = fixer.Perform();

        if (ok)
        {
            *outHandle = xbim_shape_create_from(fixer.Shape());
            if (!*outHandle)
            {
                xbim_set_error("xbim_face_fix: memory allocation failed");
                return XBIM_ERROR;
            }
            return XBIM_OK;
        }

        /* Fix failed — return original face */
        xbim_log_warning(nullptr, "xbim_face_fix: ShapeFix_Shape::Perform failed");
        *outHandle = xbim_shape_create_from(face);
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_face_fix: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion
