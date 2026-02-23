/*
 * xbim_face.cpp
 *
 * Implements face construction and query functions.
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
#include <Geom_ConicalSurface.hxx>
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
#include <ShapeFix_Edge.hxx>
#include <gp_Trsf.hxx>
#include <gp.hxx>
#include <TopLoc_Location.hxx>
#include <ShapeAnalysis_Surface.hxx>
#include <GeomLProp_SLProps.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Wire.hxx>
#include <TopExp_Explorer.hxx>
#include <TopAbs_ShapeEnum.hxx>
#include <Standard_Failure.hxx>
#include <Precision.hxx>
#include <BRepTools_WireExplorer.hxx>
#include <ShapeFix_ShapeTolerance.hxx>
#include <ShapeAnalysis_Wire.hxx>
#include <GeomLib_IsPlanarSurface.hxx>
#include <TopTools_IndexedMapOfShape.hxx>
#include <TopExp.hxx>
#include <vector>
#include <cmath>

#pragma region Face Helpers

/*
 * For non-planar surfaces, we need to add 2D parametric curves (pcurves) to
 * the wire edges so that OCCT can properly trim the surface. Returns true if
 * the resulting face area is positive (CCW orientation).
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


XBIM_EXPORT XbimResult XBIM_CALL xbim_face_build_unbounded_from_surface(
    XbimContextHandle  ctx,
    XbimSurfaceHandle  surfaceHandle,
    double             tolerance,
    XbimShapeHandle*   outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_face_build_unbounded_from_surface: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!surfaceHandle)
    {
        xbim_set_error("xbim_face_build_unbounded_from_surface: surfaceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        Handle(Geom_Surface) surface = surfaceHandle->surface;
        if (surface.IsNull())
        {
            xbim_set_error("xbim_face_build_unbounded_from_surface: surface is null");
            return XBIM_NULL_SHAPE;
        }

        BRepBuilderAPI_MakeFace faceMaker;
        faceMaker.Init(surface, Standard_False, tolerance);

        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_face_build_unbounded_from_surface: could not build face from surface");
            xbim_log_error(ctx, "Could not build unbounded face from surface");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Face face = faceMaker.Face();
        if (face.IsNull())
        {
            xbim_set_error("xbim_face_build_unbounded_from_surface: resulting face is null");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(face);
        if (!*outHandle)
        {
            xbim_set_error("xbim_face_build_unbounded_from_surface: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_face_build_unbounded_from_surface");
        xbim_set_error("xbim_face_build_unbounded_from_surface: OCCT exception");
        return XBIM_ERROR;
    }
}


/*
 * Build a bounded face from a surface with explicit parameter bounds.
 * Intended for surfaces that have infinite natural bounds in one direction
 * (e.g. Geom_SurfaceOfLinearExtrusion) where OCCT cannot build an unbounded face.
 * U bounds are taken from the surface's natural range; V bounds use [0, vMax].
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_build_surface_with_depth(
    XbimContextHandle  ctx,
    XbimSurfaceHandle  surfaceHandle,
    double             depth,
    double             tolerance,
    XbimShapeHandle*   outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_face_build_surface_with_depth: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!surfaceHandle)
    {
        xbim_set_error("xbim_face_build_surface_with_depth: surfaceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        Handle(Geom_Surface) surface = surfaceHandle->surface;
        if (surface.IsNull())
        {
            xbim_set_error("xbim_face_build_surface_with_depth: surface is null");
            return XBIM_NULL_SHAPE;
        }

        double uMin, uMax, vMin, vMax;
        surface->Bounds(uMin, uMax, vMin, vMax);

        /* For the V direction (extrusion depth), use [0, depth] instead of
           the surface's infinite natural range. */
        vMin = 0.0;
        vMax = depth;

        BRepBuilderAPI_MakeFace faceMaker(surface, uMin, uMax, vMin, vMax, tolerance);
        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_face_build_surface_with_depth: could not build bounded face");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Face face = faceMaker.Face();
        *outHandle = xbim_shape_create_from(face);
        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_face_build_surface_with_depth");
        xbim_set_error("xbim_face_build_surface_with_depth: OCCT exception");
        return XBIM_ERROR;
    }
}


/*
 * Build a bounded face from a surface using its natural parameter bounds.
 * Used for surfaces like Geom_SurfaceOfRevolution where BRepBuilderAPI_MakeFace
 * cannot construct an unbounded face but the natural bounds (U=[0,2π], V from
 * the basis curve) are finite and well-defined.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_build_surface_natural_bounds(
    XbimContextHandle  ctx,
    XbimSurfaceHandle  surfaceHandle,
    double             tolerance,
    XbimShapeHandle*   outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_face_build_surface_natural_bounds: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!surfaceHandle)
    {
        xbim_set_error("xbim_face_build_surface_natural_bounds: surfaceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        Handle(Geom_Surface) surface = surfaceHandle->surface;
        if (surface.IsNull())
        {
            xbim_set_error("xbim_face_build_surface_natural_bounds: surface is null");
            return XBIM_NULL_SHAPE;
        }

        double uMin, uMax, vMin, vMax;
        surface->Bounds(uMin, uMax, vMin, vMax);

        BRepBuilderAPI_MakeFace faceMaker(surface, uMin, uMax, vMin, vMax, tolerance);
        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_face_build_surface_natural_bounds: could not build bounded face");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Face face = faceMaker.Face();
        *outHandle = xbim_shape_create_from(face);
        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_face_build_surface_natural_bounds");
        xbim_set_error("xbim_face_build_surface_natural_bounds: OCCT exception");
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

        TopoDS_Wire wire = TopoDS::Wire(shape);
        double tolerance = Precision::Confusion();

        /* Try OCCT auto-detection first — correctly handles wires with
           curved edges (circles, arcs, etc.) and pcurves from MakeEdge2d */
        TopoDS_Face face;
        gp_Pln thePlane;
        bool havePlane = false;

        {
            BRepBuilderAPI_MakeFace autoMaker(wire, Standard_True);
            if (autoMaker.IsDone())
            {
                face = autoMaker.Face();
                havePlane = true;
            }
        }

        /* Fallback: Newell normal + barycentre plane for nearly-planar
           polygon wires where OCCT's FindPlane fails */
        if (!havePlane)
        {
            /* Extract vertices in wire traversal order */
            std::vector<gp_Pnt> pts;
            for (BRepTools_WireExplorer wEx(wire); wEx.More(); wEx.Next())
                pts.push_back(BRep_Tool::Pnt(wEx.CurrentVertex()));

            int n = (int)pts.size();
            if (n < 3)
            {
                xbim_log_warning(ctx, "Polyloop with less than 3 points is an empty loop");
                xbim_set_error("xbim_face_build_from_wire: wire has fewer than 3 vertices");
                return XBIM_NULL_SHAPE;
            }

            double nx = 0, ny = 0, nz = 0;
            for (int i = 0; i < n; i++)
            {
                const gp_Pnt& cur = pts[i];
                const gp_Pnt& nxt = pts[(i + 1) % n];
                nx += (cur.Y() - nxt.Y()) * (cur.Z() + nxt.Z());
                ny += (cur.Z() - nxt.Z()) * (cur.X() + nxt.X());
                nz += (cur.X() - nxt.X()) * (cur.Y() + nxt.Y());
            }
            double mag = std::sqrt(nx * nx + ny * ny + nz * nz);
            if (mag < gp::Resolution())
            {
                xbim_log_warning(ctx, "Polyloop is a line. Empty loop built");
                xbim_set_error("xbim_face_build_from_wire: degenerate wire normal");
                return XBIM_NULL_SHAPE;
            }

            gp_Dir normal(nx / mag, ny / mag, nz / mag);

            double cx = 0, cy = 0, cz = 0;
            for (const auto& p : pts)
            {
                cx += p.X();
                cy += p.Y();
                cz += p.Z();
            }
            gp_Pnt centre(cx / n, cy / n, cz / n);

            thePlane = gp_Pln(centre, normal);
            BRepBuilderAPI_MakeFace fallbackMaker(thePlane, wire, Standard_False);
            if (!fallbackMaker.IsDone())
            {
                xbim_set_error("xbim_face_build_from_wire: could not build face from wire");
                return XBIM_NULL_SHAPE;
            }
            face = fallbackMaker.Face();
            havePlane = true;
        }

        /* Extract vertices for self-intersection and tolerance fixes below */
        std::vector<gp_Pnt> pts;
        for (BRepTools_WireExplorer wEx(wire); wEx.More(); wEx.Next())
            pts.push_back(BRep_Tool::Pnt(wEx.CurrentVertex()));
        int n = (int)pts.size();

        /* Limit wire tolerances */
        ShapeFix_ShapeTolerance tolFixer;
        tolFixer.LimitTolerance(wire, tolerance);

        /* Fix vertex tolerances for polygons with more than 3 points
         * (triangles always fit a plane exactly) */
        if (n > 3)
        {
            TopTools_IndexedMapOfShape map;
            TopExp::MapShapes(wire, TopAbs_EDGE, map);
            ShapeFix_Edge ef;
            bool fixed = false;
            for (int i = 1; i <= map.Extent(); i++)
            {
                const TopoDS_Edge edge = TopoDS::Edge(map(i));
                if (ef.FixVertexTolerance(edge, face)) fixed = true;
            }
            if (fixed)
                xbim_log_info(ctx, "Polyloop is slightly mis-aligned to a plane. It has been adjusted");
        }

        /* Self-intersection check */
        double maxTol = BRep_Tool::MaxTolerance(wire, TopAbs_VERTEX);
        Handle(ShapeAnalysis_Wire) wa = new ShapeAnalysis_Wire(wire, face, maxTol);
        if (wa->CheckSelfIntersection())
        {
            ShapeFix_Wire wf;
            wf.Init(wa);
            wf.SetPrecision(tolerance);
            wf.SetMinTolerance(tolerance);
            wf.SetMaxTolerance(maxTol);
            if (wf.Perform())
            {
                wire = wf.Wire();
                BRepBuilderAPI_MakeFace reMaker(wire, Standard_True);
                if (!reMaker.IsDone())
                {
                    /* Retry with Newell plane if auto-detect fails after fix */
                    BRepBuilderAPI_MakeFace reMaker2(thePlane, wire, Standard_False);
                    if (reMaker2.IsDone())
                        face = reMaker2.Face();
                }
                else
                {
                    face = reMaker.Face();
                }
            }
            else
            {
                xbim_log_warning(ctx, "Failed to fix self-intersecting wire edges");
            }
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
        bool outerLoopIsCCW = add_parametric_curves(surface, outerWire, ctx->precision);

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

                bool innerLoopIsCCW = add_parametric_curves(surface, innerWire, ctx->precision);
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

        bool outerLoopIsCCW = add_parametric_curves(surface, outerWire, ctx->precision);

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

                bool innerLoopIsCCW = add_parametric_curves(surface, innerWire, ctx->precision);
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


XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_curve_bounded_plane(
    XbimContextHandle        ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    XbimShapeHandle          outerWireHandle,
    const XbimShapeHandle*   innerWireHandles,
    int                      numInnerWires,
    XbimShapeHandle*         outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_surface_build_curve_bounded_plane: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!outerWireHandle)
    {
        xbim_set_error("xbim_surface_build_curve_bounded_plane: outerWireHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        /* Build the face on the standard XOY plane at origin. The boundary wires
         * are in local 2D space (z = 0), which lies on this standard plane. */
        Handle(Geom_Plane) localPlane = new Geom_Plane(gp::XOY());
        Handle(Geom_Surface) localSurface = localPlane;

        /* Extract outer wire */
        const TopoDS_Shape& outerShape = outerWireHandle->shape;
        if (outerShape.IsNull())
        {
            xbim_set_error("xbim_surface_build_curve_bounded_plane: outer wire shape is null");
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
            xbim_set_error("xbim_surface_build_curve_bounded_plane: could not extract outer wire");
            return XBIM_INVALID_ARG;
        }

        /* Determine orientation; for planar surfaces add_parametric_curves
         * skips ShapeFix_Wire but computes the area-based CCW check. */
        bool outerIsCCW = add_parametric_curves(localSurface, outerWire, ctx->precision);

        BRepBuilderAPI_MakeFace faceMaker(
            localPlane,
            outerIsCCW ? outerWire : TopoDS::Wire(outerWire.Reversed()),
            false);

        /* Add inner boundary wires as holes */
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

                bool innerIsCCW = add_parametric_curves(localSurface, innerWire, ctx->precision);
                if (innerIsCCW)
                    innerWire.Reverse();

                faceMaker.Add(innerWire);
            }
        }

        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_surface_build_curve_bounded_plane: could not build face from wires");
            xbim_log_error(ctx, "Face specification error: could not apply bounds to plane");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Face face = faceMaker.Face();
        if (face.IsNull())
        {
            xbim_set_error("xbim_surface_build_curve_bounded_plane: resulting face is null");
            return XBIM_NULL_SHAPE;
        }

        /* Add parametric curves to edges (mirrors NSurfaceFactory::FixInvalidEdges). */
        ShapeFix_Edge edgeFixer;
        for (TopExp_Explorer exp(face, TopAbs_EDGE); exp.More(); exp.Next())
            edgeFixer.FixAddPCurve(TopoDS::Edge(exp.Current()), face, Standard_False);

        /* Displace the face from local XOY space to the world plane placement. */
        gp_Ax3 worldFrame(
            gp_Pnt(originX, originY, originZ),
            gp_Dir(zDirX,   zDirY,   zDirZ),
            gp_Dir(xDirX,   xDirY,   xDirZ));

        gp_Trsf placement;
        placement.SetDisplacement(gp::XOY(), worldFrame);
        face.Move(TopLoc_Location(placement));

        face.Closed(true);
        *outHandle = xbim_shape_create_from(face);
        if (!*outHandle)
        {
            xbim_set_error("xbim_surface_build_curve_bounded_plane: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_surface_build_curve_bounded_plane");
        xbim_set_error("xbim_surface_build_curve_bounded_plane: OCCT exception");
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

XBIM_EXPORT XbimResult XBIM_CALL xbim_face_perimeter(
    XbimShapeHandle faceHandle,
    double*         outPerimeter)
{
    xbim_clear_error();

    if (!outPerimeter)
    {
        xbim_set_error("xbim_face_perimeter: outPerimeter is NULL");
        return XBIM_INVALID_ARG;
    }
    *outPerimeter = 0.0;

    if (!faceHandle)
    {
        xbim_set_error("xbim_face_perimeter: faceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = faceHandle->shape;
        if (shape.IsNull())
        {
            xbim_set_error("xbim_face_perimeter: shape is null");
            return XBIM_NULL_SHAPE;
        }

        GProp_GProps gProps;
        BRepGProp::LinearProperties(shape, gProps);
        *outPerimeter = gProps.Mass();
        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_face_perimeter: OCCT exception");
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


XBIM_EXPORT XbimResult XBIM_CALL xbim_face_is_planar(
    XbimShapeHandle faceHandle,
    int*            outIsPlanar)
{
    xbim_clear_error();
    if (!outIsPlanar) { xbim_set_error("xbim_face_is_planar: outIsPlanar is NULL"); return XBIM_INVALID_ARG; }
    *outIsPlanar = 0;
    if (!faceHandle) { xbim_set_error("xbim_face_is_planar: faceHandle is NULL"); return XBIM_INVALID_HANDLE; }

    try
    {
        const TopoDS_Shape& shape = faceHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_FACE)
        {
            xbim_set_error("xbim_face_is_planar: handle is not a face");
            return XBIM_INVALID_ARG;
        }

        const TopoDS_Face& face = TopoDS::Face(shape);
        Handle(Geom_Surface) surf = BRep_Tool::Surface(face);
        Standard_Real tol = BRep_Tool::Tolerance(face);
        GeomLib_IsPlanarSurface ps(surf, tol);
        *outIsPlanar = ps.IsPlanar() ? 1 : 0;
        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_face_is_planar: OCCT exception");
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
    if (surf->IsKind(STANDARD_TYPE(Geom_ConicalSurface)))          return 6;  // IfcSurfaceOfRevolution (cone)
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

XBIM_EXPORT XbimResult XBIM_CALL xbim_face_normal_at_point(
    XbimContextHandle ctx,
    XbimShapeHandle faceHandle,
    double          pointX,
    double          pointY,
    double          pointZ,
    double*         outNormalX,
    double*         outNormalY,
    double*         outNormalZ)
{
    xbim_clear_error();

    if (!outNormalX || !outNormalY || !outNormalZ)
    {
        xbim_set_error("xbim_face_normal_at_point: output pointer is NULL");
        return XBIM_INVALID_ARG;
    }
    *outNormalX = 0.0;
    *outNormalY = 0.0;
    *outNormalZ = 0.0;

    if (!faceHandle)
    {
        xbim_set_error("xbim_face_normal_at_point: faceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = faceHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_FACE)
        {
            xbim_set_error("xbim_face_normal_at_point: handle is not a face");
            return XBIM_INVALID_ARG;
        }

        const TopoDS_Face& face = TopoDS::Face(shape);
        Handle(Geom_Surface) surf = BRep_Tool::Surface(face);
        if (surf.IsNull())
        {
            xbim_set_error("xbim_face_normal_at_point: face has no surface");
            return XBIM_NULL_SHAPE;
        }

        ShapeAnalysis_Surface sas(surf);
        gp_Pnt2d uv = sas.ValueOfUV(gp_Pnt(pointX, pointY, pointZ), ctx->precision);

        GeomLProp_SLProps props(surf, uv.X(), uv.Y(), 1, ctx->precision);
        if (!props.IsNormalDefined())
        {
            xbim_set_error("xbim_face_normal_at_point: normal is undefined at the given point");
            return XBIM_ERROR;
        }

        gp_Dir normal = props.Normal();

        // Respect face orientation — if reversed, flip the normal
        if (face.Orientation() == TopAbs_REVERSED)
            normal.Reverse();

        *outNormalX = normal.X();
        *outNormalY = normal.Y();
        *outNormalZ = normal.Z();

        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_face_normal_at_point: OCCT exception");
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
