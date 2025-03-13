/*
 * xbim_profile.cpp
 *
 * Implements parametric profile construction via the flat C API.
 * Ports the NWireFactory and NProfileFactory profile methods from the
 * C++/CLI engine:
 *   - Rectangle profile (wire -> face)
 *   - Circle profile (wire -> face)
 *   - Ellipse profile (wire -> face)
 *   - Rounded rectangle profile (wire with fillets -> face)
 *   - I-shape structural profile
 *   - L-shape structural profile
 *   - T-shape structural profile
 *   - U-shape structural profile
 *   - Z-shape structural profile
 *   - C-shape structural profile
 *
 * Each function builds a wire in the XY plane, creates a face from it,
 * then applies the requested axis2 placement transform.
 *
 * Arbitrary/composite/derived profiles:
 *   - Arbitrary closed profile (polyline -> face)
 *   - Arbitrary open profile (polyline -> wire)
 *   - Arbitrary profile with voids (outer face + inner wire holes)
 *   - Composite profile (merge multiple face shapes into compound)
 *   - Derived profile (apply 2D transform to parent face)
 */

#include <cmath>
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
#include <BRepBuilderAPI_MakePolygon.hxx>
#include <IntAna2d_AnaIntersection.hxx>
#include <IntAna2d_IntPoint.hxx>
#include <gp_Lin2d.hxx>
#include <gp_Pnt2d.hxx>
#include <gp_Dir2d.hxx>
#include <Standard_Failure.hxx>
#include <ShapeFix_Face.hxx>
#include <BRepBuilderAPI_Transform.hxx>
#include <BRepBuilderAPI_GTransform.hxx>
#include <gp_GTrsf.hxx>
#include <gp_Mat.hxx>
#include <TopoDS_Compound.hxx>
#include <GProp_GProps.hxx>
#include <BRepGProp.hxx>

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

/* ── Helper: apply fillets to selected vertices of a wire ────────────────── */

/*
 * Given a wire, a list of vertex indices (1-based) and their radii,
 * build fillets and return the resulting wire. If filleting fails,
 * returns the original wire unchanged.
 *
 * filletSpecs: array of { vertexIndex (1-based), radius } pairs
 * numSpecs:    number of entries
 */
struct FilletSpec { int vertexIndex; double radius; };

static TopoDS_Wire apply_fillets(const TopoDS_Wire& wire, const FilletSpec* specs, int numSpecs)
{
    BRepBuilderAPI_MakeFace faceMaker(wire, Standard_True);
    BRepFilletAPI_MakeFillet2d filleter(faceMaker.Face());

    int i = 1;
    for (BRepTools_WireExplorer exp(wire); exp.More(); exp.Next())
    {
        for (int s = 0; s < numSpecs; s++)
        {
            if (i == specs[s].vertexIndex && specs[s].radius > 0.0)
            {
                filleter.AddFillet(exp.CurrentVertex(), specs[s].radius);
                break;
            }
        }
        i++;
    }
    filleter.Build();
    if (filleter.IsDone())
    {
        TopoDS_Shape shape = filleter.Shape();
        for (TopExp_Explorer exp(shape, TopAbs_WIRE); exp.More(); )
        {
            return TopoDS::Wire(exp.Current());
        }
    }
    return wire;
}

/* ── Helper: build a face from a wire, apply placement, wrap as shape ───── */

static XbimResult make_profile_face(
    XbimContextHandle ctx,
    const TopoDS_Wire& wire,
    double originX, double originY, double originZ,
    double zDirX, double zDirY, double zDirZ,
    double xDirX, double xDirY, double xDirZ,
    XbimShapeHandle* outHandle,
    const char* funcName)
{
    BRepBuilderAPI_MakeFace faceMaker(gp_Pln(), wire, Standard_True);
    if (!faceMaker.IsDone())
    {
        xbim_set_error("face construction failed");
        xbim_log_error(ctx, "%s: face construction failed", funcName);
        return XBIM_ERROR;
    }

    TopoDS_Face face = faceMaker.Face();

    TopLoc_Location loc = make_placement(
        originX, originY, originZ,
        zDirX, zDirY, zDirZ,
        xDirX, xDirY, xDirZ);
    if (!loc.IsIdentity())
        face.Move(loc);

    *outHandle = xbim_shape_create_from(face);
    if (!*outHandle)
    {
        xbim_set_error("memory allocation failed");
        return XBIM_ERROR;
    }

    return XBIM_OK;
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

/* ── I-shape profile ─────────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_ishape(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double overallWidth, double overallDepth,
    double webThickness, double flangeThickness,
    double filletRadius,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_ishape: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (overallWidth <= 0.0 || overallDepth <= 0.0 ||
        webThickness <= 0.0 || flangeThickness <= 0.0)
    {
        xbim_set_error("xbim_profile_build_ishape: dimensions must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        double dX = overallWidth / 2.0;
        double dY = overallDepth / 2.0;
        double tF = flangeThickness;
        double tW = webThickness;

        gp_Pnt p1(-dX, dY, 0);
        gp_Pnt p2( dX, dY, 0);
        gp_Pnt p3( dX, dY - tF, 0);
        gp_Pnt p4( tW / 2.0, dY - tF, 0);
        gp_Pnt p5( tW / 2.0, -dY + tF, 0);
        gp_Pnt p6( dX, -dY + tF, 0);
        gp_Pnt p7( dX, -dY, 0);
        gp_Pnt p8(-dX, -dY, 0);
        gp_Pnt p9(-dX, -dY + tF, 0);
        gp_Pnt p10(-tW / 2.0, -dY + tF, 0);
        gp_Pnt p11(-tW / 2.0, dY - tF, 0);
        gp_Pnt p12(-dX, dY - tF, 0);

        double t = Precision::Confusion();
        BRep_Builder b;
        TopoDS_Vertex v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12;
        b.MakeVertex(v1, p1, t);
        b.MakeVertex(v2, p2, t);
        b.MakeVertex(v3, p3, t);
        b.MakeVertex(v4, p4, t);
        b.MakeVertex(v5, p5, t);
        b.MakeVertex(v6, p6, t);
        b.MakeVertex(v7, p7, t);
        b.MakeVertex(v8, p8, t);
        b.MakeVertex(v9, p9, t);
        b.MakeVertex(v10, p10, t);
        b.MakeVertex(v11, p11, t);
        b.MakeVertex(v12, p12, t);

        BRepBuilderAPI_MakePolygon polyMaker;
        polyMaker.Add(v1);  polyMaker.Add(v2);  polyMaker.Add(v3);
        polyMaker.Add(v4);  polyMaker.Add(v5);  polyMaker.Add(v6);
        polyMaker.Add(v7);  polyMaker.Add(v8);  polyMaker.Add(v9);
        polyMaker.Add(v10); polyMaker.Add(v11); polyMaker.Add(v12);
        polyMaker.Close();

        if (!polyMaker.IsDone())
        {
            xbim_set_error("xbim_profile_build_ishape: polygon construction failed");
            return XBIM_ERROR;
        }

        TopoDS_Wire wire = polyMaker.Wire();

        /* Apply fillets at web/flange junction vertices (4, 5, 10, 11) */
        if (filletRadius > 0.0)
        {
            FilletSpec specs[] = {
                {4, filletRadius}, {5, filletRadius},
                {10, filletRadius}, {11, filletRadius}
            };
            wire = apply_fillets(wire, specs, 4);
        }

        wire.Closed(Standard_True);

        return make_profile_face(ctx, wire,
            originX, originY, originZ,
            zDirX, zDirY, zDirZ,
            xDirX, xDirY, xDirZ,
            outHandle, "xbim_profile_build_ishape");
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_ishape");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_ishape: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── L-shape profile ─────────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_lshape(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double depth, double width, double thickness,
    double filletRadius, double edgeRadius, double legSlope,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_lshape: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (depth <= 0.0 || thickness <= 0.0)
    {
        xbim_set_error("xbim_profile_build_lshape: dimensions must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        double dY = depth / 2.0;
        double dX = (width > 0.0) ? width / 2.0 : dY;
        double tF = thickness;

        gp_Pnt p1(-dX, dY, 0);
        gp_Pnt p2(-dX + tF, dY, 0);
        gp_Pnt p3(-dX + tF, -dY + tF, 0);

        /* Apply leg slope if specified (legSlope > 0 means a taper angle in radians) */
        if (legSlope > 0.0)
        {
            double slopeTan = tan(legSlope);
            p3.SetX(p3.X() + (((dY * 2.0) - tF) * slopeTan));
            p3.SetY(p3.Y() + (((dX * 2.0) - tF) * slopeTan));
        }

        gp_Pnt p4( dX, -dY + tF, 0);
        gp_Pnt p5( dX, -dY, 0);
        gp_Pnt p6(-dX, -dY, 0);

        BRepBuilderAPI_MakeWire wireMaker;
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p1, p2));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p2, p3));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p3, p4));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p4, p5));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p5, p6));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p6, p1));
        TopoDS_Wire wire = wireMaker.Wire();
        wire.Closed(Standard_True);

        /* Apply fillets: vertices 2,4 get edgeRadius; vertex 3 gets filletRadius */
        if (edgeRadius > 0.0 || filletRadius > 0.0)
        {
            FilletSpec specs[] = {
                {2, edgeRadius}, {3, filletRadius}, {4, edgeRadius}
            };
            wire = apply_fillets(wire, specs, 3);
        }

        wire.Closed(Standard_True);

        return make_profile_face(ctx, wire,
            originX, originY, originZ,
            zDirX, zDirY, zDirZ,
            xDirX, xDirY, xDirZ,
            outHandle, "xbim_profile_build_lshape");
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_lshape");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_lshape: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── T-shape profile ─────────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_tshape(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double depth, double flangeWidth,
    double webThickness, double flangeThickness,
    double filletRadius, double flangeEdgeRadius, double webEdgeRadius,
    double flangeSlope, double webSlope,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_tshape: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (depth <= 0.0 || flangeWidth <= 0.0 ||
        webThickness <= 0.0 || flangeThickness <= 0.0)
    {
        xbim_set_error("xbim_profile_build_tshape: dimensions must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        double dX = flangeWidth / 2.0;
        double dY = depth / 2.0;
        double tF = flangeThickness;
        double tW = webThickness;

        gp_Pnt p1(-dX, dY, 0);
        gp_Pnt p2( dX, dY, 0);
        gp_Pnt p3( dX, dY - tF, 0);
        gp_Pnt p4( tW / 2.0, dY - tF, 0);
        gp_Pnt p5( tW / 2.0, -dY, 0);
        gp_Pnt p6(-tW / 2.0, -dY, 0);
        gp_Pnt p7(-tW / 2.0, dY - tF, 0);
        gp_Pnt p8(-dX, dY - tF, 0);

        /* Apply slopes if specified (values are in radians) */
        if (flangeSlope > 0.0 || webSlope > 0.0)
        {
            double fSlope = flangeSlope;
            double wSlope = webSlope;
            double bDiv4 = flangeWidth / 4.0;

            if (fSlope > 0.0)
            {
                double fTan = tan(fSlope);
                p3.SetY(p3.Y() + (bDiv4 * fTan));
                p8.SetY(p8.Y() + (bDiv4 * fTan));
            }

            if (fSlope > 0.0 || wSlope > 0.0)
            {
                double fTan = (fSlope > 0.0) ? tan(fSlope) : 0.0;
                double wTan = (wSlope > 0.0) ? tan(wSlope) : 0.0;

                gp_Lin2d flangeLine(gp_Pnt2d(bDiv4, dY - tF),
                                    gp_Dir2d(1.0, fTan));
                gp_Lin2d webLine(gp_Pnt2d(tW / 2.0, 0.0),
                                 gp_Dir2d(wTan, 1.0));
                IntAna2d_AnaIntersection intersector(flangeLine, webLine);
                if (intersector.NbPoints() > 0)
                {
                    gp_Pnt2d ip = intersector.Point(1).Value();
                    p4.SetX(ip.X());
                    p4.SetY(ip.Y());
                    p7.SetX(-ip.X());
                    p7.SetY(ip.Y());
                }

                if (wSlope > 0.0)
                {
                    p5.SetX(p5.X() - (dY * wTan));
                    p6.SetX(p6.X() + (dY * wTan));
                }
            }
        }

        BRepBuilderAPI_MakeWire wireMaker;
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p1, p2));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p2, p3));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p3, p4));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p4, p5));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p5, p6));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p6, p7));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p7, p8));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p8, p1));
        TopoDS_Wire wire = wireMaker.Wire();

        /* Apply fillets: 3,8=flangeEdgeRadius; 4,7=filletRadius; 5,6=webEdgeRadius */
        if (flangeEdgeRadius > 0.0 || filletRadius > 0.0 || webEdgeRadius > 0.0)
        {
            FilletSpec specs[] = {
                {3, flangeEdgeRadius}, {4, filletRadius},
                {5, webEdgeRadius},    {6, webEdgeRadius},
                {7, filletRadius},     {8, flangeEdgeRadius}
            };
            wire = apply_fillets(wire, specs, 6);
        }

        wire.Closed(Standard_True);

        return make_profile_face(ctx, wire,
            originX, originY, originZ,
            zDirX, zDirY, zDirZ,
            xDirX, xDirY, xDirZ,
            outHandle, "xbim_profile_build_tshape");
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_tshape");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_tshape: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── U-shape profile ─────────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_ushape(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double depth, double flangeWidth,
    double webThickness, double flangeThickness,
    double filletRadius, double edgeRadius, double flangeSlope,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_ushape: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (depth <= 0.0 || flangeWidth <= 0.0 ||
        webThickness <= 0.0 || flangeThickness <= 0.0)
    {
        xbim_set_error("xbim_profile_build_ushape: dimensions must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        double dX = flangeWidth / 2.0;
        double dY = depth / 2.0;
        double tF = flangeThickness;
        double tW = webThickness;

        gp_Pnt p1(-dX, dY, 0);
        gp_Pnt p2( dX, dY, 0);
        gp_Pnt p3( dX, dY - tF, 0);
        gp_Pnt p4(-dX + tW, dY - tF, 0);
        gp_Pnt p5(-dX + tW, -dY + tF, 0);
        gp_Pnt p6( dX, -dY + tF, 0);
        gp_Pnt p7( dX, -dY, 0);
        gp_Pnt p8(-dX, -dY, 0);

        /* Apply flange slope if specified (value is in radians) */
        if (flangeSlope > 0.0)
        {
            double slopeTan = tan(flangeSlope);
            p4.SetY(p4.Y() - (((dX * 2.0) - tW) * slopeTan));
            p5.SetY(p5.Y() + (((dX * 2.0) - tW) * slopeTan));
        }

        BRepBuilderAPI_MakeWire wireMaker;
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p1, p2));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p2, p3));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p3, p4));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p4, p5));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p5, p6));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p6, p7));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p7, p8));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p8, p1));
        TopoDS_Wire wire = wireMaker.Wire();
        wire.Closed(Standard_True);

        /* Apply fillets: vertices 3,6=edgeRadius; vertices 4,5=filletRadius */
        if (edgeRadius > 0.0 || filletRadius > 0.0)
        {
            FilletSpec specs[] = {
                {3, edgeRadius}, {4, filletRadius},
                {5, filletRadius}, {6, edgeRadius}
            };
            wire = apply_fillets(wire, specs, 4);
        }

        wire.Closed(Standard_True);

        return make_profile_face(ctx, wire,
            originX, originY, originZ,
            zDirX, zDirY, zDirZ,
            xDirX, xDirY, xDirZ,
            outHandle, "xbim_profile_build_ushape");
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_ushape");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_ushape: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── Z-shape profile ─────────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_zshape(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double depth, double flangeWidth,
    double webThickness, double flangeThickness,
    double filletRadius, double edgeRadius,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_zshape: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (depth <= 0.0 || flangeWidth <= 0.0 ||
        webThickness <= 0.0 || flangeThickness <= 0.0)
    {
        xbim_set_error("xbim_profile_build_zshape: dimensions must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        double dX = flangeWidth;  /* NB: flangeWidth is NOT halved (matching original) */
        double dY = depth / 2.0;
        double tF = flangeThickness;
        double tW = webThickness;

        gp_Pnt p1(-dX + (tW / 2.0), dY, 0);
        gp_Pnt p2( tW / 2.0, dY, 0);
        gp_Pnt p3( tW / 2.0, -dY + tF, 0);
        gp_Pnt p4( dX - tW / 2.0, -dY + tF, 0);
        gp_Pnt p5( dX - tW / 2.0, -dY, 0);
        gp_Pnt p6(-tW / 2.0, -dY, 0);
        gp_Pnt p7(-tW / 2.0, dY - tF, 0);
        gp_Pnt p8(-dX + (tW / 2.0), dY - tF, 0);

        BRepBuilderAPI_MakeWire wireMaker;
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p1, p2));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p2, p3));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p3, p4));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p4, p5));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p5, p6));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p6, p7));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p7, p8));
        wireMaker.Add(BRepBuilderAPI_MakeEdge(p8, p1));
        TopoDS_Wire wire = wireMaker.Wire();

        /* Apply fillets: vertices 3,7=filletRadius; vertices 4,8=edgeRadius */
        if (filletRadius > 0.0 || edgeRadius > 0.0)
        {
            FilletSpec specs[] = {
                {3, filletRadius}, {4, edgeRadius},
                {7, filletRadius}, {8, edgeRadius}
            };
            wire = apply_fillets(wire, specs, 4);
        }

        wire.Closed(Standard_True);

        return make_profile_face(ctx, wire,
            originX, originY, originZ,
            zDirX, zDirY, zDirZ,
            xDirX, xDirY, xDirZ,
            outHandle, "xbim_profile_build_zshape");
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_zshape");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_zshape: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── C-shape profile ─────────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_cshape(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double depth, double width, double wallThickness,
    double girth, double internalFilletRadius,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_cshape: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (depth <= 0.0 || width <= 0.0 || wallThickness <= 0.0)
    {
        xbim_set_error("xbim_profile_build_cshape: dimensions must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        double dX = width / 2.0;
        double dY = depth / 2.0;
        double dG = girth;
        double tW = wallThickness;

        BRepBuilderAPI_MakeWire wireMaker;

        if (dG > 0.0)
        {
            if (fabs(tW - dG) < Precision::Confusion())
            {
                /* Girth == wall thickness: 10-vertex variant */
                gp_Pnt p1(-dX, dY, 0);
                gp_Pnt p2( dX, dY, 0);
                gp_Pnt p3( dX, dY - dG, 0);
                gp_Pnt p4( dX - tW, dY - dG, 0);
                gp_Pnt p6(-dX + tW, dY - tW, 0);
                gp_Pnt p7(-dX + tW, -dY + tW, 0);
                gp_Pnt p9( dX - tW, -dY + dG, 0);
                gp_Pnt p10(dX, -dY + dG, 0);
                gp_Pnt p11(dX, -dY, 0);
                gp_Pnt p12(-dX, -dY, 0);

                wireMaker.Add(BRepBuilderAPI_MakeEdge(p1, p2));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p2, p3));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p3, p4));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p4, p6));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p6, p7));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p7, p9));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p9, p10));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p10, p11));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p11, p12));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p12, p1));
            }
            else
            {
                /* Girth != wall thickness: 12-vertex variant */
                gp_Pnt p1(-dX, dY, 0);
                gp_Pnt p2( dX, dY, 0);
                gp_Pnt p3( dX, dY - dG, 0);
                gp_Pnt p4( dX - tW, dY - dG, 0);
                gp_Pnt p5( dX - tW, dY - tW, 0);
                gp_Pnt p6(-dX + tW, dY - tW, 0);
                gp_Pnt p7(-dX + tW, -dY + tW, 0);
                gp_Pnt p8( dX - tW, -dY + tW, 0);
                gp_Pnt p9( dX - tW, -dY + dG, 0);
                gp_Pnt p10(dX, -dY + dG, 0);
                gp_Pnt p11(dX, -dY, 0);
                gp_Pnt p12(-dX, -dY, 0);

                wireMaker.Add(BRepBuilderAPI_MakeEdge(p1, p2));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p2, p3));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p3, p4));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p4, p5));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p5, p6));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p6, p7));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p7, p8));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p8, p9));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p9, p10));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p10, p11));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p11, p12));
                wireMaker.Add(BRepBuilderAPI_MakeEdge(p12, p1));
            }
        }
        else
        {
            /* No girth: 8-vertex simplified C-shape */
            gp_Pnt p1(-dX, dY, 0);
            gp_Pnt p2( dX, dY, 0);
            gp_Pnt p5( dX, dY - tW, 0);
            gp_Pnt p6(-dX + tW, dY - tW, 0);
            gp_Pnt p7(-dX + tW, -dY + tW, 0);
            gp_Pnt p8( dX, -dY + tW, 0);
            gp_Pnt p11(dX, -dY, 0);
            gp_Pnt p12(-dX, -dY, 0);

            wireMaker.Add(BRepBuilderAPI_MakeEdge(p1, p2));
            wireMaker.Add(BRepBuilderAPI_MakeEdge(p2, p5));
            wireMaker.Add(BRepBuilderAPI_MakeEdge(p5, p6));
            wireMaker.Add(BRepBuilderAPI_MakeEdge(p6, p7));
            wireMaker.Add(BRepBuilderAPI_MakeEdge(p7, p8));
            wireMaker.Add(BRepBuilderAPI_MakeEdge(p8, p11));
            wireMaker.Add(BRepBuilderAPI_MakeEdge(p11, p12));
            wireMaker.Add(BRepBuilderAPI_MakeEdge(p12, p1));
        }

        TopoDS_Wire wire = wireMaker.Wire();

        /* Apply fillets if internal fillet radius specified.
         * Original uses 12-vertex numbering for the fillet logic:
         * vertices 1,2,11,12 get outer radius (iRad + tW)
         * vertices 5,6,7,8 get inner radius (iRad)
         * For simplified variants, the same logic applies to the
         * corresponding corner positions. */
        if (internalFilletRadius > 0.0)
        {
            double iRad = internalFilletRadius;
            double oRad = iRad + tW;

            if (dG > 0.0)
            {
                /* 10 or 12 vertex variant - use same numbering as original */
                FilletSpec specs[] = {
                    {1, oRad}, {2, oRad},
                    {5, iRad}, {6, iRad}, {7, iRad}, {8, iRad},
                    {11, oRad}, {12, oRad}
                };

                if (fabs(tW - dG) < Precision::Confusion())
                {
                    /* 10-vertex: numbering is sequential 1-10 */
                    FilletSpec specs10[] = {
                        {1, oRad}, {2, oRad},
                        {4, iRad}, {5, iRad}, {6, iRad}, {7, iRad},
                        {9, oRad}, {10, oRad}
                    };
                    wire = apply_fillets(wire, specs10, 8);
                }
                else
                {
                    wire = apply_fillets(wire, specs, 8);
                }
            }
            else
            {
                /* 8-vertex: corners mapped to sequential numbering */
                FilletSpec specs8[] = {
                    {1, oRad}, {2, oRad},
                    {3, iRad}, {4, iRad}, {5, iRad}, {6, iRad},
                    {7, oRad}, {8, oRad}
                };
                wire = apply_fillets(wire, specs8, 8);
            }
        }

        wire.Closed(Standard_True);

        return make_profile_face(ctx, wire,
            originX, originY, originZ,
            zDirX, zDirY, zDirZ,
            xDirX, xDirY, xDirZ,
            outHandle, "xbim_profile_build_cshape");
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_cshape");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_cshape: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── Trapezium profile ────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_trapezium(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double bottomXDim, double topXDim, double yDim, double topXOffset,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_trapezium: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (bottomXDim <= 0.0 || topXDim <= 0.0 || yDim <= 0.0)
    {
        xbim_set_error("xbim_profile_build_trapezium: dimensions must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        /* Build trapezium in local coordinates, centered at origin.
         * Bottom edge from (-bottomXDim/2, -yDim/2) to (bottomXDim/2, -yDim/2)
         * Top edge offset by topXOffset from bottom-left:
         *   top-left  = (-bottomXDim/2 + topXOffset, yDim/2)
         *   top-right = (-bottomXDim/2 + topXOffset + topXDim, yDim/2) */
        double halfBX = bottomXDim / 2.0;
        double halfY  = yDim / 2.0;

        gp_Pnt p1(-halfBX, -halfY, 0);
        gp_Pnt p2( halfBX, -halfY, 0);
        gp_Pnt p3(-halfBX + topXOffset + topXDim,  halfY, 0);
        gp_Pnt p4(-halfBX + topXOffset,  halfY, 0);

        BRepBuilderAPI_MakePolygon polyMaker;
        polyMaker.Add(p1);
        polyMaker.Add(p2);
        polyMaker.Add(p3);
        polyMaker.Add(p4);
        polyMaker.Close();

        if (!polyMaker.IsDone())
        {
            xbim_set_error("xbim_profile_build_trapezium: polygon construction failed");
            return XBIM_ERROR;
        }

        TopoDS_Wire wire = polyMaker.Wire();
        wire.Closed(Standard_True);

        return make_profile_face(ctx, wire,
            originX, originY, originZ,
            zDirX, zDirY, zDirZ,
            xDirX, xDirY, xDirZ,
            outHandle, "xbim_profile_build_trapezium");
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_trapezium");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_trapezium: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── Asymmetric I-shape profile ──────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_asymmetric_ishape(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double bottomFlangeWidth, double overallDepth,
    double webThickness, double bottomFlangeThickness,
    double topFlangeWidth, double topFlangeThickness,
    double bottomFlangeFilletRadius, double topFlangeFilletRadius,
    double bottomFlangeEdgeRadius, double topFlangeEdgeRadius,
    double bottomFlangeSlope, double topFlangeSlope,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_asymmetric_ishape: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (bottomFlangeWidth <= 0.0 || overallDepth <= 0.0 ||
        webThickness <= 0.0 || bottomFlangeThickness <= 0.0 ||
        topFlangeWidth <= 0.0)
    {
        xbim_set_error("xbim_profile_build_asymmetric_ishape: dimensions must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        double bfW = bottomFlangeWidth / 2.0;
        double tfW = topFlangeWidth / 2.0;
        double dY  = overallDepth / 2.0;
        double bfT = bottomFlangeThickness;
        double tfT = (topFlangeThickness > 0.0) ? topFlangeThickness : bfT;
        double tW  = webThickness / 2.0;

        /* 12-vertex asymmetric I-shape profile:
         * Top flange: p1-p2 (top), p3-p12 (bottom of top flange)
         * Web: p4-p5 (right), p10-p11 (left)
         * Bottom flange: p6-p7 (top of bottom flange), p8-p9 (bottom) */
        gp_Pnt p1(-tfW,  dY, 0);
        gp_Pnt p2( tfW,  dY, 0);
        gp_Pnt p3( tfW,  dY - tfT, 0);
        gp_Pnt p4( tW,   dY - tfT, 0);
        gp_Pnt p5( tW,  -dY + bfT, 0);
        gp_Pnt p6( bfW, -dY + bfT, 0);
        gp_Pnt p7( bfW, -dY, 0);
        gp_Pnt p8(-bfW, -dY, 0);
        gp_Pnt p9(-bfW, -dY + bfT, 0);
        gp_Pnt p10(-tW, -dY + bfT, 0);
        gp_Pnt p11(-tW,  dY - tfT, 0);
        gp_Pnt p12(-tfW, dY - tfT, 0);

        /* Apply top flange slope if specified */
        if (topFlangeSlope > 0.0)
        {
            double fTan = tan(topFlangeSlope);
            double slopeAdj = (tfW / 2.0) * fTan;
            p3.SetY(p3.Y() + slopeAdj);
            p12.SetY(p12.Y() + slopeAdj);
        }

        /* Apply bottom flange slope if specified */
        if (bottomFlangeSlope > 0.0)
        {
            double fTan = tan(bottomFlangeSlope);
            double slopeAdj = (bfW / 2.0) * fTan;
            p6.SetY(p6.Y() - slopeAdj);
            p9.SetY(p9.Y() - slopeAdj);
        }

        double t = Precision::Confusion();
        BRep_Builder b;
        TopoDS_Vertex v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12;
        b.MakeVertex(v1,  p1, t);
        b.MakeVertex(v2,  p2, t);
        b.MakeVertex(v3,  p3, t);
        b.MakeVertex(v4,  p4, t);
        b.MakeVertex(v5,  p5, t);
        b.MakeVertex(v6,  p6, t);
        b.MakeVertex(v7,  p7, t);
        b.MakeVertex(v8,  p8, t);
        b.MakeVertex(v9,  p9, t);
        b.MakeVertex(v10, p10, t);
        b.MakeVertex(v11, p11, t);
        b.MakeVertex(v12, p12, t);

        BRepBuilderAPI_MakePolygon polyMaker;
        polyMaker.Add(v1);  polyMaker.Add(v2);  polyMaker.Add(v3);
        polyMaker.Add(v4);  polyMaker.Add(v5);  polyMaker.Add(v6);
        polyMaker.Add(v7);  polyMaker.Add(v8);  polyMaker.Add(v9);
        polyMaker.Add(v10); polyMaker.Add(v11); polyMaker.Add(v12);
        polyMaker.Close();

        if (!polyMaker.IsDone())
        {
            xbim_set_error("xbim_profile_build_asymmetric_ishape: polygon construction failed");
            return XBIM_ERROR;
        }

        TopoDS_Wire wire = polyMaker.Wire();

        /* Apply fillets:
         * Top flange: 4,11 = topFlangeFilletRadius; 3,12 = topFlangeEdgeRadius
         * Bottom flange: 5,10 = bottomFlangeFilletRadius; 6,9 = bottomFlangeEdgeRadius */
        if (topFlangeFilletRadius > 0.0 || topFlangeEdgeRadius > 0.0 ||
            bottomFlangeFilletRadius > 0.0 || bottomFlangeEdgeRadius > 0.0)
        {
            FilletSpec specs[] = {
                {3, topFlangeEdgeRadius},     {4, topFlangeFilletRadius},
                {5, bottomFlangeFilletRadius}, {6, bottomFlangeEdgeRadius},
                {9, bottomFlangeEdgeRadius},   {10, bottomFlangeFilletRadius},
                {11, topFlangeFilletRadius},   {12, topFlangeEdgeRadius}
            };
            wire = apply_fillets(wire, specs, 8);
        }

        wire.Closed(Standard_True);

        return make_profile_face(ctx, wire,
            originX, originY, originZ,
            zDirX, zDirY, zDirZ,
            xDirX, xDirY, xDirZ,
            outHandle, "xbim_profile_build_asymmetric_ishape");
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_asymmetric_ishape");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_asymmetric_ishape: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── Rectangle hollow profile ────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_rectangle_hollow(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double xDim,    double yDim,    double wallThickness,
    double innerFilletRadius, double outerFilletRadius,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_rectangle_hollow: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (xDim <= 0.0 || yDim <= 0.0 || wallThickness <= 0.0)
    {
        xbim_set_error("xbim_profile_build_rectangle_hollow: dimensions must be positive");
        return XBIM_INVALID_ARG;
    }

    if (wallThickness >= xDim / 2.0 || wallThickness >= yDim / 2.0)
    {
        xbim_set_error("xbim_profile_build_rectangle_hollow: wall thickness too large for given dimensions");
        return XBIM_INVALID_ARG;
    }

    try
    {
        double precision = Precision::Confusion();
        double xOff = xDim / 2.0;
        double yOff = yDim / 2.0;

        /* Build outer rectangle wire */
        gp_Pnt bl(-xOff, -yOff, 0);
        gp_Pnt br( xOff, -yOff, 0);
        gp_Pnt tr( xOff,  yOff, 0);
        gp_Pnt tl(-xOff,  yOff, 0);

        BRep_Builder builder;
        TopoDS_Vertex vbl, vbr, vtr, vtl;
        builder.MakeVertex(vbl, bl, precision);
        builder.MakeVertex(vbr, br, precision);
        builder.MakeVertex(vtr, tr, precision);
        builder.MakeVertex(vtl, tl, precision);

        TopoDS_Wire outerWire;
        builder.MakeWire(outerWire);
        builder.Add(outerWire, BRepBuilderAPI_MakeEdge(vbl, vbr));
        builder.Add(outerWire, BRepBuilderAPI_MakeEdge(vbr, vtr));
        builder.Add(outerWire, BRepBuilderAPI_MakeEdge(vtr, vtl));
        builder.Add(outerWire, BRepBuilderAPI_MakeEdge(vtl, vbl));
        outerWire.Closed(Standard_True);

        /* Apply outer fillets if specified */
        if (outerFilletRadius > 0.0)
        {
            BRepBuilderAPI_MakeFace outerFaceMaker(gp_Pln(), outerWire, Standard_True);
            BRepFilletAPI_MakeFillet2d filleter(outerFaceMaker.Face());
            for (BRepTools_WireExplorer exp(outerWire); exp.More(); exp.Next())
            {
                filleter.AddFillet(exp.CurrentVertex(), outerFilletRadius);
            }
            filleter.Build();
            if (filleter.IsDone())
            {
                TopoDS_Shape shape = filleter.Shape();
                for (TopExp_Explorer exp(shape, TopAbs_WIRE); exp.More(); )
                {
                    outerWire = TopoDS::Wire(exp.Current());
                    break;
                }
            }
        }

        /* Make face from outer wire */
        BRepBuilderAPI_MakeFace faceMaker(gp_Pln(), outerWire, Standard_True);

        /* Build inner rectangle wire (inset by wallThickness) */
        double t = wallThickness;
        gp_Pnt ibl(-xOff + t, -yOff + t, 0);
        gp_Pnt ibr( xOff - t, -yOff + t, 0);
        gp_Pnt itr( xOff - t,  yOff - t, 0);
        gp_Pnt itl(-xOff + t,  yOff - t, 0);

        TopoDS_Vertex vibl, vibr, vitr, vitl;
        builder.MakeVertex(vibl, ibl, precision);
        builder.MakeVertex(vibr, ibr, precision);
        builder.MakeVertex(vitr, itr, precision);
        builder.MakeVertex(vitl, itl, precision);

        TopoDS_Wire innerWire;
        builder.MakeWire(innerWire);
        builder.Add(innerWire, BRepBuilderAPI_MakeEdge(vibl, vibr));
        builder.Add(innerWire, BRepBuilderAPI_MakeEdge(vibr, vitr));
        builder.Add(innerWire, BRepBuilderAPI_MakeEdge(vitr, vitl));
        builder.Add(innerWire, BRepBuilderAPI_MakeEdge(vitl, vibl));

        /* Apply inner fillets if specified */
        if (innerFilletRadius > 0.0)
        {
            BRepBuilderAPI_MakeFace innerFaceMaker(gp_Pln(), innerWire, Standard_True);
            BRepFilletAPI_MakeFillet2d filleter(innerFaceMaker.Face());
            for (BRepTools_WireExplorer exp(innerWire); exp.More(); exp.Next())
            {
                filleter.AddFillet(exp.CurrentVertex(), innerFilletRadius);
            }
            filleter.Build();
            if (filleter.IsDone())
            {
                TopoDS_Shape shape = filleter.Shape();
                for (TopExp_Explorer exp(shape, TopAbs_WIRE); exp.More(); )
                {
                    innerWire = TopoDS::Wire(exp.Current());
                    break;
                }
            }
        }

        /* Reverse inner wire so it forms a hole, then add to face */
        innerWire.Reverse();
        innerWire.Closed(Standard_True);
        faceMaker.Add(innerWire);

        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_profile_build_rectangle_hollow: face construction failed");
            xbim_log_error(ctx, "Could not build rectangle hollow profile face");
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
            xbim_set_error("xbim_profile_build_rectangle_hollow: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_rectangle_hollow");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_rectangle_hollow: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── Circle hollow profile ───────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_circle_hollow(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,  double wallThickness,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_circle_hollow: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (radius <= 0.0 || wallThickness <= 0.0)
    {
        xbim_set_error("xbim_profile_build_circle_hollow: dimensions must be positive");
        return XBIM_INVALID_ARG;
    }

    if (wallThickness >= radius)
    {
        xbim_set_error("xbim_profile_build_circle_hollow: wall thickness must be less than radius");
        return XBIM_INVALID_ARG;
    }

    try
    {
        /* Build outer circle */
        gp_Ax2 ax2(gp_Pnt(0, 0, 0), gp::DZ(), gp::DX());
        Handle(Geom_Circle) outerCircle = GC_MakeCircle(ax2, radius);
        TopoDS_Edge outerEdge = BRepBuilderAPI_MakeEdge(outerCircle);
        TopoDS_Wire outerWire = BRepBuilderAPI_MakeWire(outerEdge);
        outerWire.Closed(true);

        /* Make face from outer wire */
        BRepBuilderAPI_MakeFace faceMaker(gp_Pln(), outerWire, Standard_True);

        /* Build inner circle */
        double innerRadius = radius - wallThickness;
        Handle(Geom_Circle) innerCircle = GC_MakeCircle(ax2, innerRadius);
        TopoDS_Edge innerEdge = BRepBuilderAPI_MakeEdge(innerCircle);
        TopoDS_Wire innerWire = BRepBuilderAPI_MakeWire(innerEdge);
        innerWire.Closed(true);

        /* Reverse inner wire so it forms a hole, then add to face */
        innerWire.Reverse();
        faceMaker.Add(innerWire);

        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_profile_build_circle_hollow: face construction failed");
            xbim_log_error(ctx, "Could not build circle hollow profile face");
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
            xbim_set_error("xbim_profile_build_circle_hollow: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_circle_hollow");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_circle_hollow: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── Arbitrary closed profile ──────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_arbitrary_closed(
    XbimContextHandle ctx,
    const double* pointsX,
    const double* pointsY,
    int pointCount,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_arbitrary_closed: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!pointsX || !pointsY)
    {
        xbim_set_error("xbim_profile_build_arbitrary_closed: point arrays are NULL");
        return XBIM_INVALID_ARG;
    }

    if (pointCount < 3)
    {
        xbim_set_error("xbim_profile_build_arbitrary_closed: need at least 3 points");
        return XBIM_INVALID_ARG;
    }

    try
    {
        /* Build a closed polygon wire from the 2D point array */
        BRepBuilderAPI_MakePolygon polyMaker;
        for (int i = 0; i < pointCount; i++)
        {
            polyMaker.Add(gp_Pnt(pointsX[i], pointsY[i], 0.0));
        }
        polyMaker.Close();

        if (!polyMaker.IsDone())
        {
            xbim_set_error("xbim_profile_build_arbitrary_closed: polygon construction failed");
            xbim_log_error(ctx, "Could not build arbitrary closed profile polygon");
            return XBIM_ERROR;
        }

        TopoDS_Wire wire = polyMaker.Wire();

        /* Ensure counter-clockwise winding by checking face area sign */
        BRepBuilderAPI_MakeFace faceMaker(gp_Pln(), wire, Standard_True);
        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_profile_build_arbitrary_closed: face construction failed");
            xbim_log_error(ctx, "Could not build arbitrary closed profile face");
            return XBIM_ERROR;
        }

        TopoDS_Face face = faceMaker.Face();

        /* Check area - if negative, the wire is clockwise and we need to reverse */
        GProp_GProps props;
        BRepGProp::SurfaceProperties(face, props);
        if (props.Mass() < 0.0)
        {
            wire.Reverse();
            BRepBuilderAPI_MakeFace faceMaker2(gp_Pln(), wire, Standard_True);
            if (!faceMaker2.IsDone())
            {
                xbim_set_error("xbim_profile_build_arbitrary_closed: face reconstruction failed after reversal");
                return XBIM_ERROR;
            }
            face = faceMaker2.Face();
        }

        *outHandle = xbim_shape_create_from(face);
        if (!*outHandle)
        {
            xbim_set_error("xbim_profile_build_arbitrary_closed: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_arbitrary_closed");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_arbitrary_closed: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── Arbitrary open profile (wire, not face) ─────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_arbitrary_open(
    XbimContextHandle ctx,
    const double* pointsX,
    const double* pointsY,
    int pointCount,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_arbitrary_open: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!pointsX || !pointsY)
    {
        xbim_set_error("xbim_profile_build_arbitrary_open: point arrays are NULL");
        return XBIM_INVALID_ARG;
    }

    if (pointCount < 2)
    {
        xbim_set_error("xbim_profile_build_arbitrary_open: need at least 2 points");
        return XBIM_INVALID_ARG;
    }

    try
    {
        /* Build an open polyline wire from the 2D point array */
        BRepBuilderAPI_MakeWire wireMaker;
        for (int i = 0; i < pointCount - 1; i++)
        {
            gp_Pnt p1(pointsX[i], pointsY[i], 0.0);
            gp_Pnt p2(pointsX[i + 1], pointsY[i + 1], 0.0);

            if (p1.Distance(p2) < Precision::Confusion())
                continue;

            wireMaker.Add(BRepBuilderAPI_MakeEdge(p1, p2));
        }

        if (!wireMaker.IsDone())
        {
            xbim_set_error("xbim_profile_build_arbitrary_open: wire construction failed");
            xbim_log_error(ctx, "Could not build arbitrary open profile wire");
            return XBIM_ERROR;
        }

        TopoDS_Wire wire = wireMaker.Wire();

        *outHandle = xbim_shape_create_from(wire);
        if (!*outHandle)
        {
            xbim_set_error("xbim_profile_build_arbitrary_open: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_arbitrary_open");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_arbitrary_open: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── Profile with voids (outer face + inner wire holes) ──────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_with_voids(
    XbimContextHandle ctx,
    XbimShapeHandle outerFaceHandle,
    const XbimShapeHandle* innerWireHandles,
    int numInnerWires,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_with_voids: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!outerFaceHandle)
    {
        xbim_set_error("xbim_profile_build_with_voids: outerFaceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    if (!innerWireHandles || numInnerWires < 1)
    {
        xbim_set_error("xbim_profile_build_with_voids: need at least 1 inner wire");
        return XBIM_INVALID_ARG;
    }

    try
    {
        /* Get the outer face */
        const TopoDS_Shape& outerShape = outerFaceHandle->shape;
        if (outerShape.IsNull() || outerShape.ShapeType() != TopAbs_FACE)
        {
            xbim_set_error("xbim_profile_build_with_voids: outer handle is not a face");
            return XBIM_INVALID_ARG;
        }

        /* Extract the outer wire from the face */
        TopoDS_Wire outerWire;
        for (TopExp_Explorer exp(outerShape, TopAbs_WIRE); exp.More(); exp.Next())
        {
            outerWire = TopoDS::Wire(exp.Current());
            break;  /* Take the first (outer) wire */
        }

        if (outerWire.IsNull())
        {
            xbim_set_error("xbim_profile_build_with_voids: could not extract outer wire from face");
            return XBIM_ERROR;
        }

        /* Build a new face from the outer wire */
        BRepBuilderAPI_MakeFace faceMaker(gp_Pln(), outerWire, Standard_True);

        /* Add each inner wire as a hole */
        for (int i = 0; i < numInnerWires; i++)
        {
            if (!innerWireHandles[i])
            {
                xbim_log_warning(ctx, "xbim_profile_build_with_voids: inner wire %d is NULL, skipping", i);
                continue;
            }

            const TopoDS_Shape& innerShape = innerWireHandles[i]->shape;
            if (innerShape.IsNull())
            {
                xbim_log_warning(ctx, "xbim_profile_build_with_voids: inner wire %d shape is null, skipping", i);
                continue;
            }

            /* Inner shape can be a wire or a face (extract wire from face) */
            TopoDS_Wire innerWire;
            if (innerShape.ShapeType() == TopAbs_WIRE)
            {
                innerWire = TopoDS::Wire(innerShape);
            }
            else if (innerShape.ShapeType() == TopAbs_FACE)
            {
                for (TopExp_Explorer exp(innerShape, TopAbs_WIRE); exp.More(); exp.Next())
                {
                    innerWire = TopoDS::Wire(exp.Current());
                    break;
                }
            }
            else
            {
                xbim_log_warning(ctx, "xbim_profile_build_with_voids: inner shape %d is not a wire or face, skipping", i);
                continue;
            }

            if (innerWire.IsNull())
                continue;

            /* Ensure inner wire is reversed (clockwise = hole) */
            innerWire.Reverse();
            faceMaker.Add(innerWire);
        }

        if (!faceMaker.IsDone())
        {
            xbim_set_error("xbim_profile_build_with_voids: face construction with voids failed");
            xbim_log_error(ctx, "Could not build profile face with voids");
            return XBIM_ERROR;
        }

        /* Fix orientation to handle any winding issues */
        ShapeFix_Face fixFace(faceMaker.Face());
        fixFace.FixOrientation();
        TopoDS_Face face = fixFace.Face();

        *outHandle = xbim_shape_create_from(face);
        if (!*outHandle)
        {
            xbim_set_error("xbim_profile_build_with_voids: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_with_voids");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_with_voids: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── Composite profile (multiple profiles merged into a compound) ── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_composite(
    XbimContextHandle ctx,
    const XbimShapeHandle* profileHandles,
    int numProfiles,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_composite: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!profileHandles || numProfiles < 1)
    {
        xbim_set_error("xbim_profile_build_composite: need at least 1 profile");
        return XBIM_INVALID_ARG;
    }

    try
    {
        BRep_Builder builder;
        TopoDS_Compound compound;
        builder.MakeCompound(compound);

        int addedCount = 0;
        for (int i = 0; i < numProfiles; i++)
        {
            if (!profileHandles[i])
            {
                xbim_log_warning(ctx, "xbim_profile_build_composite: profile %d is NULL, skipping", i);
                continue;
            }

            const TopoDS_Shape& shape = profileHandles[i]->shape;
            if (shape.IsNull())
            {
                xbim_log_warning(ctx, "xbim_profile_build_composite: profile %d shape is null, skipping", i);
                continue;
            }

            builder.Add(compound, shape);
            addedCount++;
        }

        if (addedCount == 0)
        {
            xbim_set_error("xbim_profile_build_composite: no valid profiles to combine");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(compound);
        if (!*outHandle)
        {
            xbim_set_error("xbim_profile_build_composite: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_composite");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_composite: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── Derived profile (apply 2D transform to parent face) ─────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_derived(
    XbimContextHandle ctx,
    XbimShapeHandle parentHandle,
    double m00, double m01, double m02,
    double m10, double m11, double m12,
    int isNonUniformScale,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_derived: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!parentHandle)
    {
        xbim_set_error("xbim_profile_build_derived: parentHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& parentShape = parentHandle->shape;
        if (parentShape.IsNull())
        {
            xbim_set_error("xbim_profile_build_derived: parent shape is null");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Shape resultShape;

        if (isNonUniformScale)
        {
            /* Non-uniform scaling: use gp_GTrsf */
            gp_GTrsf gtrsf;
            /* Set 2D transform in the XY plane (Z row stays identity) */
            gtrsf.SetValue(1, 1, m00);
            gtrsf.SetValue(1, 2, m01);
            gtrsf.SetValue(1, 3, m02);
            gtrsf.SetValue(2, 1, m10);
            gtrsf.SetValue(2, 2, m11);
            gtrsf.SetValue(2, 3, m12);
            /* Row 3 defaults to (0, 0, 1, 0) = identity in Z */

            BRepBuilderAPI_GTransform gTransform(parentShape, gtrsf, Standard_True);
            if (!gTransform.IsDone())
            {
                xbim_set_error("xbim_profile_build_derived: non-uniform transform failed");
                xbim_log_error(ctx, "Could not apply non-uniform transform to derived profile");
                return XBIM_ERROR;
            }
            resultShape = gTransform.Shape();
        }
        else
        {
            /* Uniform transform: use gp_Trsf for better performance */
            gp_Trsf trsf;

            /* Build the 2D affine transform as a 3D transform in the XY plane.
             * The matrix [[m00, m01, m02], [m10, m11, m12]] maps to:
             *   rotation + scale + translation in XY
             * with Z left unchanged.
             */
            gp_Mat mat(
                m00, m01, 0.0,
                m10, m11, 0.0,
                0.0, 0.0, 1.0);
            gp_XYZ trans(m02, m12, 0.0);
            trsf.SetValues(
                mat.Value(1, 1), mat.Value(1, 2), mat.Value(1, 3), trans.X(),
                mat.Value(2, 1), mat.Value(2, 2), mat.Value(2, 3), trans.Y(),
                mat.Value(3, 1), mat.Value(3, 2), mat.Value(3, 3), trans.Z());

            BRepBuilderAPI_Transform brepTransform(parentShape, trsf, Standard_True);
            if (!brepTransform.IsDone())
            {
                xbim_set_error("xbim_profile_build_derived: uniform transform failed");
                xbim_log_error(ctx, "Could not apply uniform transform to derived profile");
                return XBIM_ERROR;
            }
            resultShape = brepTransform.Shape();
        }

        *outHandle = xbim_shape_create_from(resultShape);
        if (!*outHandle)
        {
            xbim_set_error("xbim_profile_build_derived: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_derived");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_derived: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── Mirrored profile (mirror parent face about Y axis) ──────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_mirrored(
    XbimContextHandle ctx,
    XbimShapeHandle parentHandle,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_profile_build_mirrored: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!parentHandle)
    {
        xbim_set_error("xbim_profile_build_mirrored: parentHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& parentShape = parentHandle->shape;
        if (parentShape.IsNull())
        {
            xbim_set_error("xbim_profile_build_mirrored: parent shape is null");
            return XBIM_NULL_SHAPE;
        }

        /* Mirror about Y axis (matching original NProfileFactory::BuildMirrored):
         * axis origin = (0,0,0), axis direction = (0,1,0) */
        gp_Ax1 mirrorAxis(gp_Pnt(0, 0, 0), gp_Dir(0, 1, 0));

        gp_Trsf mirrorTrsf;
        mirrorTrsf.SetMirror(mirrorAxis);

        BRepBuilderAPI_Transform brepTransform(parentShape, mirrorTrsf, Standard_True);
        if (!brepTransform.IsDone())
        {
            xbim_set_error("xbim_profile_build_mirrored: mirror transform failed");
            xbim_log_error(ctx, "Could not apply mirror transform to profile");
            return XBIM_ERROR;
        }

        /* Reverse the result to fix face normal orientation after mirror */
        TopoDS_Shape resultShape = brepTransform.Shape().Reversed();

        *outHandle = xbim_shape_create_from(resultShape);
        if (!*outHandle)
        {
            xbim_set_error("xbim_profile_build_mirrored: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_profile_build_mirrored");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_profile_build_mirrored: OCCT exception");
        return XBIM_ERROR;
    }
}
