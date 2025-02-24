/*
 * xbim_profile.cpp
 *
 * Implements basic parametric profile construction via the flat C API.
 * Ports the NWireFactory and NProfileFactory profile methods from the
 * C++/CLI engine:
 *   - Rectangle profile (wire -> face)
 *   - Circle profile (wire -> face)
 *   - Ellipse profile (wire -> face)
 *   - Rounded rectangle profile (wire with fillets -> face)
 *
 * Each function builds a wire in the XY plane, creates a face from it,
 * then applies the requested axis2 placement transform.
 */

#include "xbim_profile.h"
#include "xbim_shape.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <gp_Ax2.hxx>
#include <gp_Ax3.hxx>
#include <gp_Pnt.hxx>
#include <gp_Dir.hxx>
#include <gp_Pln.hxx>
#include <gp_Trsf.hxx>
#include <GC_MakeSegment.hxx>
#include <GC_MakeCircle.hxx>
#include <GC_MakeEllipse.hxx>
#include <Geom_TrimmedCurve.hxx>
#include <Geom_Circle.hxx>
#include <Geom_Ellipse.hxx>
#include <BRep_Builder.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepFilletAPI_MakeFillet2d.hxx>
#include <BRepTools_WireExplorer.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Wire.hxx>
#include <TopoDS_Face.hxx>
#include <TopExp_Explorer.hxx>
#include <TopLoc_Location.hxx>
#include <Precision.hxx>
#include <Standard_Failure.hxx>

/* ── Helper: build placement transform from 9 doubles ────────────────────── */

static TopLoc_Location make_placement(
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ)
{
    gp_Ax2 ax2(
        gp_Pnt(originX, originY, originZ),
        gp_Dir(zDirX, zDirY, zDirZ),
        gp_Dir(xDirX, xDirY, xDirZ));

    gp_Trsf trsf;
    trsf.SetTransformation(gp_Ax3(ax2));
    trsf.Invert();
    return TopLoc_Location(trsf);
}

/* ── Rectangle profile ───────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_rectangle(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double xDim,    double yDim,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_rectangle: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (xDim <= 0.0 || yDim <= 0.0)
    {
        xbim_set_error("xbim_profile_build_rectangle: dimensions must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        double xOff = xDim / 2.0;
        double yOff = yDim / 2.0;

        gp_Pnt bl(-xOff, -yOff, 0);
        gp_Pnt br( xOff, -yOff, 0);
        gp_Pnt tr( xOff,  yOff, 0);
        gp_Pnt tl(-xOff,  yOff, 0);

        Handle(Geom_TrimmedCurve) seg1 = GC_MakeSegment(bl, br);
        Handle(Geom_TrimmedCurve) seg2 = GC_MakeSegment(br, tr);
        Handle(Geom_TrimmedCurve) seg3 = GC_MakeSegment(tr, tl);
        Handle(Geom_TrimmedCurve) seg4 = GC_MakeSegment(tl, bl);

        TopoDS_Edge e1 = BRepBuilderAPI_MakeEdge(seg1);
        TopoDS_Edge e2 = BRepBuilderAPI_MakeEdge(seg2);
        TopoDS_Edge e3 = BRepBuilderAPI_MakeEdge(seg3);
        TopoDS_Edge e4 = BRepBuilderAPI_MakeEdge(seg4);

        TopoDS_Wire wire = BRepBuilderAPI_MakeWire(e1, e2, e3, e4);
        wire.Closed(true);

        BRepBuilderAPI_MakeFace faceMaker(gp_Pln(), wire, Standard_True);
        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_profile_build_rectangle: face construction failed");
            xbim_log_error(ctx, "Could not build rectangle profile face");
            return XBIM_ERROR;
        }

        TopoDS_Face face = faceMaker.Face();

        /* Apply placement transform */
        TopLoc_Location loc = make_placement(
            originX, originY, originZ,
            zDirX, zDirY, zDirZ,
            xDirX, xDirY, xDirZ);
        if (!loc.IsIdentity())
            face.Move(loc);

        *outHandle = xbim_shape_create_from(face);
        if (!*outHandle)
        {
            xbim_set_error("xbim_profile_build_rectangle: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_rectangle");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_rectangle: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── Circle profile ──────────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_circle(
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
        xbim_set_error("xbim_profile_build_circle: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (radius <= 0.0)
    {
        xbim_set_error("xbim_profile_build_circle: radius must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        /* Build circle in the XY plane at origin */
        gp_Ax2 ax2(gp_Pnt(0, 0, 0), gp::DZ(), gp::DX());
        Handle(Geom_Circle) hCirc = GC_MakeCircle(ax2, radius);

        TopoDS_Edge edge = BRepBuilderAPI_MakeEdge(hCirc);
        TopoDS_Wire wire = BRepBuilderAPI_MakeWire(edge);
        wire.Closed(true);

        BRepBuilderAPI_MakeFace faceMaker(gp_Pln(), wire, Standard_True);
        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_profile_build_circle: face construction failed");
            xbim_log_error(ctx, "Could not build circle profile face");
            return XBIM_ERROR;
        }

        TopoDS_Face face = faceMaker.Face();

        /* Apply placement transform */
        TopLoc_Location loc = make_placement(
            originX, originY, originZ,
            zDirX, zDirY, zDirZ,
            xDirX, xDirY, xDirZ);
        if (!loc.IsIdentity())
            face.Move(loc);

        *outHandle = xbim_shape_create_from(face);
        if (!*outHandle)
        {
            xbim_set_error("xbim_profile_build_circle: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_circle");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_circle: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── Ellipse profile ─────────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_ellipse(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double semiAxis1,  double semiAxis2,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_ellipse: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (semiAxis1 <= 0.0 || semiAxis2 <= 0.0)
    {
        xbim_set_error("xbim_profile_build_ellipse: semi-axes must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        /* Build ellipse in the XY plane at origin.
         * OCCT requires majorRadius >= minorRadius, so swap if needed
         * and adjust the axis orientation accordingly. */
        double majorR = semiAxis1;
        double minorR = semiAxis2;
        gp_Dir xDir = gp::DX();

        if (semiAxis2 > semiAxis1)
        {
            majorR = semiAxis2;
            minorR = semiAxis1;
            xDir = gp::DY();
        }

        gp_Ax2 ax2(gp_Pnt(0, 0, 0), gp::DZ(), xDir);
        Handle(Geom_Ellipse) hEllipse = new Geom_Ellipse(ax2, majorR, minorR);

        TopoDS_Edge edge = BRepBuilderAPI_MakeEdge(hEllipse);
        TopoDS_Wire wire = BRepBuilderAPI_MakeWire(edge);
        wire.Closed(true);

        BRepBuilderAPI_MakeFace faceMaker(gp_Pln(), wire, Standard_True);
        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_profile_build_ellipse: face construction failed");
            xbim_log_error(ctx, "Could not build ellipse profile face");
            return XBIM_ERROR;
        }

        TopoDS_Face face = faceMaker.Face();

        /* Apply placement transform */
        TopLoc_Location loc = make_placement(
            originX, originY, originZ,
            zDirX, zDirY, zDirZ,
            xDirX, xDirY, xDirZ);
        if (!loc.IsIdentity())
            face.Move(loc);

        *outHandle = xbim_shape_create_from(face);
        if (!*outHandle)
        {
            xbim_set_error("xbim_profile_build_ellipse: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_ellipse");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_ellipse: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── Rounded rectangle profile ───────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_rounded_rectangle(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double xDim,    double yDim,    double roundingRadius,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_rounded_rectangle: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (xDim <= 0.0 || yDim <= 0.0)
    {
        xbim_set_error("xbim_profile_build_rounded_rectangle: dimensions must be positive");
        return XBIM_INVALID_ARG;
    }

    if (roundingRadius < 0.0)
    {
        xbim_set_error("xbim_profile_build_rounded_rectangle: rounding radius must be non-negative");
        return XBIM_INVALID_ARG;
    }

    try
    {
        double xOff = xDim / 2.0;
        double yOff = yDim / 2.0;
        double precision = Precision::Confusion();

        gp_Pnt bl(-xOff, -yOff, 0);
        gp_Pnt br( xOff, -yOff, 0);
        gp_Pnt tr( xOff,  yOff, 0);
        gp_Pnt tl(-xOff,  yOff, 0);

        /* Build the rectangle wire using BRep_Builder + explicit vertices
         * (matching NProfileFactory::BuildRoundedRectangle pattern). */
        BRep_Builder builder;
        TopoDS_Vertex vbl, vbr, vtr, vtl;
        builder.MakeVertex(vbl, bl, precision);
        builder.MakeVertex(vbr, br, precision);
        builder.MakeVertex(vtr, tr, precision);
        builder.MakeVertex(vtl, tl, precision);

        TopoDS_Wire wire;
        builder.MakeWire(wire);
        builder.Add(wire, BRepBuilderAPI_MakeEdge(vbl, vbr));
        builder.Add(wire, BRepBuilderAPI_MakeEdge(vbr, vtr));
        builder.Add(wire, BRepBuilderAPI_MakeEdge(vtr, vtl));
        builder.Add(wire, BRepBuilderAPI_MakeEdge(vtl, vbl));
        wire.Closed(true);

        /* Apply fillets if rounding radius is positive */
        if (roundingRadius > 0.0)
        {
            BRepBuilderAPI_MakeFace tempFaceMaker(gp_Pln(), wire, Standard_True);
            BRepFilletAPI_MakeFillet2d filleter(tempFaceMaker.Face());

            for (BRepTools_WireExplorer exp(wire); exp.More(); exp.Next())
            {
                filleter.AddFillet(exp.CurrentVertex(), roundingRadius);
            }
            filleter.Build();

            if (filleter.IsDone())
            {
                TopoDS_Shape shape = filleter.Shape();
                for (TopExp_Explorer exp(shape, TopAbs_WIRE); exp.More(); )
                {
                    wire = TopoDS::Wire(exp.Current());
                    break;
                }
            }
        }

        /* Build the face from the (possibly filleted) wire */
        BRepBuilderAPI_MakeFace faceMaker(gp_Pln(), wire, Standard_True);
        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_profile_build_rounded_rectangle: face construction failed");
            xbim_log_error(ctx, "Could not build rounded rectangle profile face");
            return XBIM_ERROR;
        }

        TopoDS_Face face = faceMaker.Face();

        /* Apply placement transform */
        TopLoc_Location loc = make_placement(
            originX, originY, originZ,
            zDirX, zDirY, zDirZ,
            xDirX, xDirY, xDirZ);
        if (!loc.IsIdentity())
            face.Move(loc);

        *outHandle = xbim_shape_create_from(face);
        if (!*outHandle)
        {
            xbim_set_error("xbim_profile_build_rounded_rectangle: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_rounded_rectangle");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_rounded_rectangle: OCCT exception");
        return XBIM_ERROR;
    }
}
