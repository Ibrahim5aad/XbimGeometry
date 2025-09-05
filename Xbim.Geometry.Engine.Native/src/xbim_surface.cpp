/*
 * xbim_surface.cpp
 *
 * Implements surface construction, query, and lifecycle functions via the flat C API.
 * Ports NSurfaceFactory methods from the C++/CLI engine:
 *   - Build plane from origin and normal (or axis-2 placement)
 *   - Build cylindrical surface from axis-2 placement and radius
 *   - Build spherical surface from axis-2 placement and radius
 *   - Build B-spline surface from control points grid, knots, and parameters
 *   - Destroy surface handle
 */

#include "xbim_surface.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <gp_Pnt.hxx>
#include <gp_Dir.hxx>
#include <gp_Ax2.hxx>
#include <gp_Ax3.hxx>
#include <Precision.hxx>
#include <Geom_Plane.hxx>
#include <Geom_CylindricalSurface.hxx>
#include <Geom_SphericalSurface.hxx>
#include <Geom_BSplineSurface.hxx>
#include <TColgp_Array2OfPnt.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <TColStd_Array1OfInteger.hxx>
#include <TColStd_Array2OfReal.hxx>
#include <Standard_Failure.hxx>

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

        Handle(Geom_Plane) plane = new Geom_Plane(origin, normal);

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

#pragma endregion
