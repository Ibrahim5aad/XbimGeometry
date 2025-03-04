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
#include <BRepBuilderAPI_MakePolygon.hxx>
#include <IntAna2d_AnaIntersection.hxx>
#include <IntAna2d_IntPoint.hxx>
#include <gp_Lin2d.hxx>
#include <gp_Pnt2d.hxx>
#include <gp_Dir2d.hxx>
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
