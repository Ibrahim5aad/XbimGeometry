/*
 * xbim_surface.cpp
 *
 * Implements surface construction, query, and lifecycle functions
 * 
 */

#include "xbim_surface.h"
#include "xbim_curve.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"
#include "xbim_shape.h"
#include "xbim_location.h"

#include <gp_Pnt.hxx>
#include <gp_Dir.hxx>
#include <gp_Ax2.hxx>
#include <gp_Ax3.hxx>
#include <Precision.hxx>
#include <Geom_Plane.hxx>
#include <Geom_CylindricalSurface.hxx>
#include <Geom_SphericalSurface.hxx>
#include <Geom_BSplineSurface.hxx>
#include <Geom_SurfaceOfLinearExtrusion.hxx>
#include <Geom_TrimmedCurve.hxx>
#include <Geom_SurfaceOfRevolution.hxx>
#include <TColgp_Array2OfPnt.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <TColStd_Array1OfInteger.hxx>
#include <TColStd_Array2OfReal.hxx>
#include <Standard_Failure.hxx>

#include <BRepBuilderAPI_MakePolygon.hxx>
#include <BRepBuilderAPI_Sewing.hxx>
#include <BRepFill.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Wire.hxx>

#pragma region Surface Helpers

XbimSurfaceHandle xbim_surface_create_from(const Handle(Geom_Surface)& surface)
{
    if (surface.IsNull())
        return nullptr;

    auto* wrapper = new (std::nothrow) XbimSurface_;
    if (!wrapper)
        return nullptr;

    wrapper->surface = surface;
    return wrapper;
}

#pragma endregion

#pragma region Surface Construction

XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_destroy(XbimSurfaceHandle handle)
{
    if (!handle)
        return XBIM_OK; /* safe no-op */

    delete handle;
    return XBIM_OK;
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_plane(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double normalX, double normalY, double normalZ,
    double refDirX,  double refDirY,  double refDirZ,
    XbimSurfaceHandle* outHandle,
    double* outRefDirX, double* outRefDirY, double* outRefDirZ)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_surface_build_plane: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    try
    {
        gp_Pnt origin(originX, originY, originZ);
        gp_Dir normal(normalX, normalY, normalZ);

        /* Use reference direction if provided, otherwise let OCCT auto-compute */
        double refMag = refDirX * refDirX + refDirY * refDirY + refDirZ * refDirZ;
        Handle(Geom_Plane) plane;
        if (refMag > 1e-14)
        {
            gp_Dir refDir(refDirX, refDirY, refDirZ);
            gp_Ax3 ax3(origin, normal, refDir);
            plane = new Geom_Plane(ax3);
        }
        else
        {
            plane = new Geom_Plane(origin, normal);
        }

        /* Output the reference direction (X-axis) that OCCT computed */
        if (outRefDirX && outRefDirY && outRefDirZ)
        {
            const gp_Dir& xDir = plane->Position().XDirection();
            *outRefDirX = xDir.X();
            *outRefDirY = xDir.Y();
            *outRefDirZ = xDir.Z();
        }

        *outHandle = xbim_surface_create_from(plane);
        if (!*outHandle)
        {
            xbim_set_error("xbim_surface_build_plane: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_surface_build_plane");
        xbim_set_error("xbim_surface_build_plane: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_cylindrical(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    XbimSurfaceHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_surface_build_cylindrical: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (radius <= Precision::Confusion())
    {
        xbim_set_error("xbim_surface_build_cylindrical: radius must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        gp_Pnt origin(originX, originY, originZ);
        gp_Dir zDir(zDirX, zDirY, zDirZ);
        gp_Dir xDir(xDirX, xDirY, xDirZ);
        gp_Ax3 ax3(origin, zDir, xDir);

        Handle(Geom_CylindricalSurface) cylinder = new Geom_CylindricalSurface(ax3, radius);

        *outHandle = xbim_surface_create_from(cylinder);
        if (!*outHandle)
        {
            xbim_set_error("xbim_surface_build_cylindrical: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_surface_build_cylindrical");
        xbim_set_error("xbim_surface_build_cylindrical: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_spherical(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    XbimSurfaceHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_surface_build_spherical: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (radius <= Precision::Confusion())
    {
        xbim_set_error("xbim_surface_build_spherical: radius must be positive");
        return XBIM_INVALID_ARG;
    }

    try
    {
        gp_Pnt origin(originX, originY, originZ);
        gp_Dir zDir(zDirX, zDirY, zDirZ);
        gp_Dir xDir(xDirX, xDirY, xDirZ);
        gp_Ax3 ax3(origin, zDir, xDir);

        Handle(Geom_SphericalSurface) sphere = new Geom_SphericalSurface(ax3, radius);

        *outHandle = xbim_surface_create_from(sphere);
        if (!*outHandle)
        {
            xbim_set_error("xbim_surface_build_spherical: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_surface_build_spherical");
        xbim_set_error("xbim_surface_build_spherical: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_bspline(
    XbimContextHandle ctx,
    const double*     polesXYZ,
    int               numPolesU,
    int               numPolesV,
    const double*     uKnots,
    int               numUKnots,
    const double*     vKnots,
    int               numVKnots,
    const int*        uMultiplicities,
    const int*        vMultiplicities,
    int               uDegree,
    int               vDegree,
    const double*     weights,
    XbimSurfaceHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_surface_build_bspline: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!polesXYZ || numPolesU < 2 || numPolesV < 2)
    {
        xbim_set_error("xbim_surface_build_bspline: invalid poles");
        return XBIM_INVALID_ARG;
    }
    if (!uKnots || numUKnots < 2 || !vKnots || numVKnots < 2)
    {
        xbim_set_error("xbim_surface_build_bspline: invalid knots");
        return XBIM_INVALID_ARG;
    }
    if (!uMultiplicities || !vMultiplicities)
    {
        xbim_set_error("xbim_surface_build_bspline: multiplicities are NULL");
        return XBIM_INVALID_ARG;
    }
    if (uDegree < 1 || vDegree < 1)
    {
        xbim_set_error("xbim_surface_build_bspline: degrees must be >= 1");
        return XBIM_INVALID_ARG;
    }

    try
    {
        /* Poles are stored row-major: [u0v0, u0v1, ..., u0vN, u1v0, ...] */
        TColgp_Array2OfPnt poles(1, numPolesU, 1, numPolesV);
        for (int u = 0; u < numPolesU; u++)
        {
            for (int v = 0; v < numPolesV; v++)
            {
                int idx = (u * numPolesV + v) * 3;
                poles.SetValue(u + 1, v + 1, gp_Pnt(polesXYZ[idx], polesXYZ[idx + 1], polesXYZ[idx + 2]));
            }
        }

        TColStd_Array1OfReal uKnotArr(1, numUKnots);
        for (int i = 0; i < numUKnots; i++)
            uKnotArr.SetValue(i + 1, uKnots[i]);

        TColStd_Array1OfReal vKnotArr(1, numVKnots);
        for (int i = 0; i < numVKnots; i++)
            vKnotArr.SetValue(i + 1, vKnots[i]);

        TColStd_Array1OfInteger uMultArr(1, numUKnots);
        for (int i = 0; i < numUKnots; i++)
            uMultArr.SetValue(i + 1, uMultiplicities[i]);

        TColStd_Array1OfInteger vMultArr(1, numVKnots);
        for (int i = 0; i < numVKnots; i++)
            vMultArr.SetValue(i + 1, vMultiplicities[i]);

        Handle(Geom_BSplineSurface) bspline;

        if (weights)
        {
            TColStd_Array2OfReal weightArr(1, numPolesU, 1, numPolesV);
            for (int u = 0; u < numPolesU; u++)
                for (int v = 0; v < numPolesV; v++)
                    weightArr.SetValue(u + 1, v + 1, weights[u * numPolesV + v]);

            bspline = new Geom_BSplineSurface(
                poles, weightArr, uKnotArr, vKnotArr, uMultArr, vMultArr, uDegree, vDegree);
        }
        else
        {
            bspline = new Geom_BSplineSurface(
                poles, uKnotArr, vKnotArr, uMultArr, vMultArr, uDegree, vDegree);
        }

        *outHandle = xbim_surface_create_from(bspline);
        if (!*outHandle)
        {
            xbim_set_error("xbim_surface_build_bspline: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_surface_build_bspline");
        xbim_set_error("xbim_surface_build_bspline: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_sectioned(
    XbimContextHandle          ctx,
    const double*              pointsXYZ,
    int                        numSections,
    int                        numPointsPerSection,
    const XbimLocationHandle*  locations,
    XbimShapeHandle*           outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_surface_build_sectioned: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!pointsXYZ || numSections < 2 || numPointsPerSection < 2)
    {
        xbim_set_error("xbim_surface_build_sectioned: invalid points or section count");
        return XBIM_INVALID_ARG;
    }
    if (!locations)
    {
        xbim_set_error("xbim_surface_build_sectioned: locations is NULL");
        return XBIM_INVALID_ARG;
    }

    try
    {
        // For each consecutive pair of point indices (tags), create a longitudinal
        // wire connecting that point across all sections, then build ruled surface
        // strips between adjacent wires.
        BRepBuilderAPI_Sewing outerSewing;

        for (int t = 0; t < numPointsPerSection - 1; ++t)
        {
            // Build longitudinal wire for point index t
            BRepBuilderAPI_MakePolygon poly1;
            // Build longitudinal wire for point index t+1
            BRepBuilderAPI_MakePolygon poly2;

            for (int s = 0; s < numSections; ++s)
            {
                const auto& loc = locations[s]->location;
                const gp_Trsf& trsf = loc.Transformation();

                int idx1 = (s * numPointsPerSection + t) * 3;
                gp_Pnt p1(pointsXYZ[idx1], pointsXYZ[idx1 + 1], pointsXYZ[idx1 + 2]);
                p1.Transform(trsf);
                poly1.Add(p1);

                int idx2 = (s * numPointsPerSection + (t + 1)) * 3;
                gp_Pnt p2(pointsXYZ[idx2], pointsXYZ[idx2 + 1], pointsXYZ[idx2 + 2]);
                p2.Transform(trsf);
                poly2.Add(p2);
            }

            if (!poly1.IsDone() || !poly2.IsDone())
            {
                xbim_set_error("xbim_surface_build_sectioned: failed to build longitudinal wire");
                return XBIM_ERROR;
            }

            TopoDS_Wire wire1 = poly1.Wire();
            TopoDS_Wire wire2 = poly2.Wire();

            // Create ruled surfaces between matching edges of the two wires
            std::vector<TopoDS_Edge> edges1, edges2;
            for (TopExp_Explorer exp(wire1, TopAbs_EDGE); exp.More(); exp.Next())
                edges1.push_back(TopoDS::Edge(exp.Current()));
            for (TopExp_Explorer exp(wire2, TopAbs_EDGE); exp.More(); exp.Next())
                edges2.push_back(TopoDS::Edge(exp.Current()));

            BRepBuilderAPI_Sewing stripSewing;
            size_t edgeCount = std::min(edges1.size(), edges2.size());
            for (size_t e = 0; e < edgeCount; ++e)
            {
                TopoDS_Shape ruled = BRepFill::Face(edges1[e], edges2[e]);
                stripSewing.Add(ruled);
            }
            stripSewing.Perform();

            outerSewing.Add(stripSewing.SewedShape());
        }

        outerSewing.Perform();
        TopoDS_Shape result = outerSewing.SewedShape();

        *outHandle = xbim_shape_create_from(result);
        if (!*outHandle)
        {
            xbim_set_error("xbim_surface_build_sectioned: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_surface_build_sectioned");
        xbim_set_error("xbim_surface_build_sectioned: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_revolution(
    XbimContextHandle ctx,
    XbimCurveHandle   curveHandle,
    double axisOriginX, double axisOriginY, double axisOriginZ,
    double axisDirX,    double axisDirY,    double axisDirZ,
    XbimSurfaceHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_surface_build_revolution: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!curveHandle || curveHandle->curve.IsNull())
    {
        xbim_set_error("xbim_surface_build_revolution: curveHandle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        gp_Pnt origin(axisOriginX, axisOriginY, axisOriginZ);
        gp_Dir dir(axisDirX, axisDirY, axisDirZ);
        gp_Ax1 axis(origin, dir);

        Handle(Geom_SurfaceOfRevolution) revSurface =
            new Geom_SurfaceOfRevolution(curveHandle->curve, axis);

        if (revSurface.IsNull())
        {
            xbim_set_error("xbim_surface_build_revolution: failed to create surface");
            return XBIM_ERROR;
        }

        *outHandle = xbim_surface_create_from(revSurface);
        if (!*outHandle)
        {
            xbim_set_error("xbim_surface_build_revolution: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_surface_build_revolution");
        xbim_set_error("xbim_surface_build_revolution: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_linear_extrusion(
    XbimContextHandle ctx,
    XbimCurveHandle   curveHandle,
    double dirX,    double dirY,    double dirZ,
    double posOX,   double posOY,   double posOZ,
    double posZX,   double posZY,   double posZZ,
    double posXX,   double posXY,   double posXZ,
    int    hasPosition,
    XbimSurfaceHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_surface_build_linear_extrusion: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!curveHandle || curveHandle->curve.IsNull())
    {
        xbim_set_error("xbim_surface_build_linear_extrusion: curveHandle is NULL or invalid");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        gp_Dir extDir(dirX, dirY, dirZ);

        /* Unwrap any Geom_TrimmedCurve layers to the full basis curve.
         * The surface must cover the entire basis curve domain because
         * consumers such as IfcSurfaceCurveSweptAreaSolid may define a
         * directrix that extends beyond the original trim bounds. */
        Handle(Geom_Curve) curve = curveHandle->curve;
        Handle(Geom_TrimmedCurve) tc = Handle(Geom_TrimmedCurve)::DownCast(curve);
        while (!tc.IsNull())
        {
            curve = tc->BasisCurve();
            tc = Handle(Geom_TrimmedCurve)::DownCast(curve);
        }

        Handle(Geom_SurfaceOfLinearExtrusion) surface =
            new Geom_SurfaceOfLinearExtrusion(curve, extDir);

        if (surface.IsNull())
        {
            xbim_set_error("xbim_surface_build_linear_extrusion: failed to create surface");
            return XBIM_ERROR;
        }

        /* Apply the IIfcSweptSurface.Position transform if present.
         * SetTransformation(ax3) maps FROM global INTO the local frame;
         * we invert to get from local to global (place geometry at position). */
        if (hasPosition)
        {
            gp_Ax3 targetFrame(
                gp_Pnt(posOX, posOY, posOZ),
                gp_Dir(posZX, posZY, posZZ),
                gp_Dir(posXX, posXY, posXZ));
            gp_Trsf trsf;
            trsf.SetTransformation(targetFrame);
            trsf.Invert();
            surface->Transform(trsf);
        }

        *outHandle = xbim_surface_create_from(surface);
        if (!*outHandle)
        {
            xbim_set_error("xbim_surface_build_linear_extrusion: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_surface_build_linear_extrusion");
        xbim_set_error("xbim_surface_build_linear_extrusion: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion
