/*
 * xbim_grid.cpp
 *
 * Creates a visual representation of an IFC grid by sweeping a small
 * rectangular cross-section along each grid axis curve.
 */

#include "xbim_geometry_api.h"
#include "xbim_shape.h"
#include "xbim_curve2d.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <vector>
#include <algorithm>
#include <cmath>

#include <gp_Pnt2d.hxx>
#include <gp_Vec2d.hxx>
#include <gp_Dir2d.hxx>
#include <gp_Lin2d.hxx>
#include <gp_Pnt.hxx>
#include <gp_Vec.hxx>
#include <gp_Ax2.hxx>
#include <gp_Pln.hxx>

#include <Geom2d_Curve.hxx>
#include <Geom2d_Line.hxx>
#include <Geom2d_TrimmedCurve.hxx>
#include <Geom2dAPI_InterCurveCurve.hxx>
#include <IntAna2d_AnaIntersection.hxx>
#include <GeomLib.hxx>

#include <BRep_Builder.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepOffsetAPI_MakePipe.hxx>
#include <TopoDS_Wire.hxx>
#include <TopoDS_Compound.hxx>

/* ── Helpers ────────────────────────────────────────────── */

/**
 * Collect intersection points between two sets of 2D curves.
 */
static void collect_intersections(
    const std::vector<Handle(Geom2d_Curve)>& setA,
    const std::vector<Handle(Geom2d_Curve)>& setB,
    double precision,
    std::vector<gp_Pnt2d>& pts)
{
    for (const auto& a : setA)
    {
        for (const auto& b : setB)
        {
            try
            {
                Geom2dAPI_InterCurveCurve inter(a, b, precision);
                for (int i = 1; i <= inter.NbPoints(); i++)
                    pts.push_back(inter.Point(i));
            }
            catch (...) { /* skip problematic pairs */ }
        }
    }
}

/**
 * Build a closed rectangular wire (width x depth) at the given location,
 * perpendicular to the curve tangent direction.
 *
 *   centre   – start point of the curve
 *   tangent  – normalised tangent at centre
 *   normal   – normalised in-plane normal (perpendicular to tangent)
 */
static TopoDS_Wire make_rect_wire_at(
    const gp_Pnt& centre, const gp_Vec& tangent, const gp_Vec& normal,
    double halfWidth, double halfDepth)
{
    /* vertical = tangent x normal  (out-of-plane direction) */
    gp_Vec vertical = tangent.Crossed(normal);

    gp_Pnt p0 = centre.Translated(normal * (-halfWidth) + vertical * (-halfDepth));
    gp_Pnt p1 = centre.Translated(normal * ( halfWidth) + vertical * (-halfDepth));
    gp_Pnt p2 = centre.Translated(normal * ( halfWidth) + vertical * ( halfDepth));
    gp_Pnt p3 = centre.Translated(normal * (-halfWidth) + vertical * ( halfDepth));

    BRepBuilderAPI_MakeEdge e0(p0, p1);
    BRepBuilderAPI_MakeEdge e1(p1, p2);
    BRepBuilderAPI_MakeEdge e2(p2, p3);
    BRepBuilderAPI_MakeEdge e3(p3, p0);

    BRep_Builder bb;
    TopoDS_Wire wire;
    bb.MakeWire(wire);
    bb.Add(wire, e0.Edge());
    bb.Add(wire, e1.Edge());
    bb.Add(wire, e2.Edge());
    bb.Add(wire, e3.Edge());
    return wire;
}

/**
 * Try to trim an unbounded 2D line to the bounding box.
 * Returns a trimmed copy if the line intersects the bbox, or nullptr.
 */
static Handle(Geom2d_Curve) trim_line_to_bbox(
    const Handle(Geom2d_Curve)& curve,
    double bbX, double bbY, double bbSizeX, double bbSizeY)
{
    if (!curve->IsKind(STANDARD_TYPE(Geom2d_Line)))
        return curve; /* non-lines are already bounded */

    Handle(Geom2d_Line) line = Handle(Geom2d_Line)::DownCast(curve);
    gp_Lin2d lin2d = line->Lin2d();

    gp_Lin2d top   (gp_Pnt2d(bbX, bbY + bbSizeY), gp_Dir2d(1, 0));
    gp_Lin2d bottom(gp_Pnt2d(bbX, bbY),           gp_Dir2d(1, 0));
    gp_Lin2d left  (gp_Pnt2d(bbX, bbY),           gp_Dir2d(0, 1));
    gp_Lin2d right (gp_Pnt2d(bbX + bbSizeX, bbY), gp_Dir2d(0, 1));

    std::vector<double> params;
    IntAna2d_AnaIntersection its;

    auto try_intersect = [&](const gp_Lin2d& boundary) {
        its.Perform(lin2d, boundary);
        if (its.NbPoints() > 0)
        {
            double p = its.Point(1).ParamOnFirst();
            if (!std::isnan(p))
                params.push_back(p);
        }
    };

    try_intersect(top);
    try_intersect(bottom);
    if (params.size() < 2) try_intersect(left);
    if (params.size() < 2) try_intersect(right);

    if (params.size() < 2)
        return nullptr; /* can't trim – no two intersections */

    double pMin = *std::min_element(params.begin(), params.end());
    double pMax = *std::max_element(params.begin(), params.end());

    if (std::abs(pMax - pMin) < 1e-10)
        return nullptr; /* degenerate */

    return new Geom2d_TrimmedCurve(curve, pMin, pMax);
}

/**
 * Extract Handle(Geom2d_Curve) from an array of XbimCurve2dHandle.
 */
static void extract_curves(
    const XbimCurve2dHandle* handles, int count,
    std::vector<Handle(Geom2d_Curve)>& out)
{
    for (int i = 0; i < count; i++)
    {
        if (handles[i] && handles[i]->curve)
            out.push_back(handles[i]->curve);
    }
}


/* ── Main export ──────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_grid_create(
    XbimContextHandle       ctx,
    const XbimCurve2dHandle* uCurves, int uCount,
    const XbimCurve2dHandle* vCurves, int vCount,
    const XbimCurve2dHandle* wCurves, int wCount,
    XbimShapeHandle*        outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_grid_create: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    try
    {
        double precision = ctx->precision;
        double mm = std::max(ctx->oneMillimeter, ctx->precision * 10.0);

        /* ── 1. Collect curves ── */
        std::vector<Handle(Geom2d_Curve)> uVec, vVec, wVec;
        extract_curves(uCurves, uCount, uVec);
        extract_curves(vCurves, vCount, vVec);
        extract_curves(wCurves, wCount, wVec);

        /* ── 2. Compute intersections ── */
        std::vector<gp_Pnt2d> intersections;
        collect_intersections(uVec, vVec, precision, intersections);
        collect_intersections(uVec, wVec, precision, intersections);
        collect_intersections(vVec, wVec, precision, intersections);

        /* ── 3. Compute bounding box ── */
        double bbMinX = 0, bbMinY = 0, bbMaxX = 0, bbMaxY = 0;
        bool bbInit = false;
        for (const auto& pt : intersections)
        {
            if (!bbInit)
            {
                bbMinX = bbMaxX = pt.X();
                bbMinY = bbMaxY = pt.Y();
                bbInit = true;
            }
            else
            {
                bbMinX = std::min(bbMinX, pt.X());
                bbMinY = std::min(bbMinY, pt.Y());
                bbMaxX = std::max(bbMaxX, pt.X());
                bbMaxY = std::max(bbMaxY, pt.Y());
            }
        }

        double bbSizeX = bbMaxX - bbMinX;
        double bbSizeY = bbMaxY - bbMinY;

        if (bbSizeX < precision || bbSizeY < precision)
        {
            /* Near-zero extent: inflate to 150mm default */
            xbim_log_warning(ctx, "Grid extent is near zero (%d intersections). "
                "Inflating to default extent.", (int)intersections.size());
            double cx = (bbMinX + bbMaxX) * 0.5;
            double cy = (bbMinY + bbMaxY) * 0.5;
            double half = 75.0 * mm;
            bbMinX = cx - half;
            bbMinY = cy - half;
            bbSizeX = 150.0 * mm;
            bbSizeY = 150.0 * mm;
        }
        else
        {
            /* Inflate by 20% */
            double padX = bbSizeX * 0.2;
            double padY = bbSizeY * 0.2;
            bbMinX -= padX;
            bbMinY -= padY;
            bbSizeX += 2.0 * padX;
            bbSizeY += 2.0 * padY;
        }

        /* ── 4. Concatenate all curves ── */
        std::vector<Handle(Geom2d_Curve)> allCurves;
        allCurves.insert(allCurves.end(), uVec.begin(), uVec.end());
        allCurves.insert(allCurves.end(), vVec.begin(), vVec.end());
        allCurves.insert(allCurves.end(), wVec.begin(), wVec.end());

        /* ── 5. Rectangular profile dimensions ── */
        double halfWidth = 75.0 * mm * 0.5;
        double halfDepth = mm * 0.5;

        /* (Profile wire is built per-curve at the curve's start point) */

        /* ── 6. Sweep each curve ── */
        BRep_Builder compBuilder;
        TopoDS_Compound compound;
        compBuilder.MakeCompound(compound);
        bool anyFailed = false;

        for (const auto& curve2d : allCurves)
        {
            try
            {
                /* Trim unbounded lines to bbox */
                Handle(Geom2d_Curve) trimmed = trim_line_to_bbox(
                    curve2d, bbMinX, bbMinY, bbSizeX, bbSizeY);
                if (trimmed.IsNull())
                    continue;

                /* Get start point and tangent */
                gp_Pnt2d origin2d;
                gp_Vec2d tangent2d;
                trimmed->D1(trimmed->FirstParameter(), origin2d, tangent2d);

                if (tangent2d.Magnitude() < 1e-10)
                    continue; /* degenerate */

                tangent2d.Normalize();
                gp_Vec2d normal2d = gp_Vec2d(-tangent2d.Y(), tangent2d.X());

                /* Convert 2D curve to 3D edge */
                Handle(Geom_Curve) curve3d = GeomLib::To3d(gp_Ax2(), trimmed);
                if (curve3d.IsNull())
                    continue;

                BRepBuilderAPI_MakeEdge edgeMaker(curve3d);
                if (!edgeMaker.IsDone())
                    continue;

                BRepBuilderAPI_MakeWire wireMaker(edgeMaker.Edge());
                if (!wireMaker.IsDone())
                    continue;

                TopoDS_Wire spine = wireMaker.Wire();

                /* Build rectangular profile at curve start */
                gp_Pnt origin3d(origin2d.X(), origin2d.Y(), 0.0);
                gp_Vec tangent3d(tangent2d.X(), tangent2d.Y(), 0.0);
                gp_Vec normal3dDir(normal2d.X(), normal2d.Y(), 0.0);

                TopoDS_Wire profileWire = make_rect_wire_at(
                    origin3d, tangent3d, normal3dDir, halfWidth, halfDepth);

                /* Build a face from the closed profile wire */
                BRepBuilderAPI_MakeFace faceMaker(profileWire, Standard_True);
                if (!faceMaker.IsDone())
                {
                    anyFailed = true;
                    continue;
                }

                /* Sweep the face along the spine → produces a solid directly */
                BRepOffsetAPI_MakePipe pipeMaker(spine, faceMaker.Face());
                pipeMaker.Build();

                if (!pipeMaker.IsDone())
                {
                    anyFailed = true;
                    continue;
                }

                compBuilder.Add(compound, pipeMaker.Shape());
            }
            catch (const Standard_Failure& e)
            {
                xbim_log_occt_failure(ctx, e, "xbim_grid_create: axis sweep");
                anyFailed = true;
            }
            catch (...)
            {
                anyFailed = true;
            }
        }

        if (anyFailed)
            xbim_log_warning(ctx,
                "One or more grid axis sweeps failed to convert successfully");

        *outHandle = xbim_shape_create_from(compound);
        if (!*outHandle)
        {
            xbim_set_error("xbim_grid_create: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_grid_create");
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_grid_create: OCCT exception");
        return XBIM_ERROR;
    }
    catch (...)
    {
        xbim_set_error("xbim_grid_create: unexpected exception");
        return XBIM_ERROR;
    }
}
