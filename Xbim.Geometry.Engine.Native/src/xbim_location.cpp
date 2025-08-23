/*
 * xbim_location.cpp
 *
 * Implements the location/transform handle lifecycle and operations.
 * Each XbimLocationHandle wraps a heap-allocated TopLoc_Location with
 * operations for creation from axis placement, identity, composition,
 * destruction, and applying a transform to a shape.
 */

#include "xbim_location.h"
#include "xbim_shape.h"
#include "xbim_error.h"

#include <new>

#include <gp_Ax3.hxx>
#include <gp_Pnt.hxx>
#include <gp_Dir.hxx>
#include <gp_Trsf.hxx>
#include <TopLoc_Location.hxx>
#include <Standard_Failure.hxx>

/* ── Internal helper ──────────────────────────────────────────────────────── */

XbimLocationHandle xbim_location_create_internal(const TopLoc_Location& loc)
{
    XbimLocation_* l = new (std::nothrow) XbimLocation_();
    if (!l) return nullptr;
    l->location = loc;
    return l;
}

/* ── Public API ───────────────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_location_create_from_axis2(
    double originX, double originY, double originZ,
    double zDirX, double zDirY, double zDirZ,
    double xDirX, double xDirY, double xDirZ,
    XbimLocationHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_location_create_from_axis2: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }

    *outHandle = nullptr;

    try
    {
        gp_Pnt origin(originX, originY, originZ);
        gp_Dir zDir(zDirX, zDirY, zDirZ);
        gp_Dir xDir(xDirX, xDirY, xDirZ);

        /* Build a transform that maps geometry from its local frame (defined by
         * the given axis placement) to global coordinates. This is the standard
         * interpretation for IFC IfcAxis2Placement3D.
         *
         * gp_Trsf::SetTransformation(ax3) computes T such that T maps points
         * FROM the global frame INTO the local frame. We want the inverse:
         * from local to global (i.e., place geometry at the given position).
         * So we compute the single-arg form and invert it. */
        gp_Ax3 targetFrame(origin, zDir, xDir);
        gp_Trsf trsf;
        trsf.SetTransformation(targetFrame);
        trsf.Invert();

        TopLoc_Location loc(trsf);
        *outHandle = xbim_location_create_internal(loc);

        if (!*outHandle)
        {
            xbim_set_error("xbim_location_create_from_axis2: allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (Standard_Failure& e)
    {
        xbim_set_error(e.GetMessageString() ? e.GetMessageString()
                       : "xbim_location_create_from_axis2: OCCT exception");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_location_create_identity(
    XbimLocationHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_location_create_identity: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }

    TopLoc_Location identity;
    *outHandle = xbim_location_create_internal(identity);

    if (!*outHandle)
    {
        xbim_set_error("xbim_location_create_identity: allocation failed");
        return XBIM_ERROR;
    }

    return XBIM_OK;
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_location_compose(
    XbimLocationHandle loc1,
    XbimLocationHandle loc2,
    XbimLocationHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_location_compose: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }

    *outHandle = nullptr;

    if (!loc1)
    {
        xbim_set_error("xbim_location_compose: loc1 is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (!loc2)
    {
        xbim_set_error("xbim_location_compose: loc2 is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        TopLoc_Location composed = loc1->location * loc2->location;
        *outHandle = xbim_location_create_internal(composed);

        if (!*outHandle)
        {
            xbim_set_error("xbim_location_compose: allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (Standard_Failure& e)
    {
        xbim_set_error(e.GetMessageString() ? e.GetMessageString()
                       : "xbim_location_compose: OCCT exception");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_location_destroy(XbimLocationHandle handle)
{
    xbim_clear_error();

    if (!handle)
        return XBIM_OK; /* destroying null is a safe no-op */

    delete handle;
    return XBIM_OK;
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_get_location(
    XbimShapeHandle     shapeHandle,
    XbimLocationHandle* outHandle,
    double* outM11, double* outM12, double* outM13,
    double* outM21, double* outM22, double* outM23,
    double* outM31, double* outM32, double* outM33,
    double* outOffsetX, double* outOffsetY, double* outOffsetZ,
    double* outScale)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_shape_get_location: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }

    *outHandle = nullptr;

    if (!shapeHandle)
    {
        xbim_set_error("xbim_shape_get_location: shapeHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (shapeHandle->shape.IsNull())
    {
        xbim_set_error("xbim_shape_get_location: shape is null");
        return XBIM_NULL_SHAPE;
    }

    try
    {
        const TopLoc_Location& loc = shapeHandle->shape.Location();
        *outHandle = xbim_location_create_internal(loc);

        if (!*outHandle)
        {
            xbim_set_error("xbim_shape_get_location: allocation failed");
            return XBIM_ERROR;
        }

        if (loc.IsIdentity())
        {
            if (outM11) *outM11 = 1; if (outM12) *outM12 = 0; if (outM13) *outM13 = 0;
            if (outM21) *outM21 = 0; if (outM22) *outM22 = 1; if (outM23) *outM23 = 0;
            if (outM31) *outM31 = 0; if (outM32) *outM32 = 0; if (outM33) *outM33 = 1;
            if (outOffsetX) *outOffsetX = 0; if (outOffsetY) *outOffsetY = 0; if (outOffsetZ) *outOffsetZ = 0;
            if (outScale) *outScale = 1.0;
        }
        else
        {
            const gp_Trsf& trsf = loc.Transformation();
            if (outM11) *outM11 = trsf.Value(1,1); if (outM12) *outM12 = trsf.Value(1,2); if (outM13) *outM13 = trsf.Value(1,3);
            if (outM21) *outM21 = trsf.Value(2,1); if (outM22) *outM22 = trsf.Value(2,2); if (outM23) *outM23 = trsf.Value(2,3);
            if (outM31) *outM31 = trsf.Value(3,1); if (outM32) *outM32 = trsf.Value(3,2); if (outM33) *outM33 = trsf.Value(3,3);
            if (outOffsetX) *outOffsetX = trsf.Value(1,4);
            if (outOffsetY) *outOffsetY = trsf.Value(2,4);
            if (outOffsetZ) *outOffsetZ = trsf.Value(3,4);
            if (outScale) *outScale = trsf.ScaleFactor();
        }

        return XBIM_OK;
    }
    catch (Standard_Failure& e)
    {
        xbim_set_error(e.GetMessageString() ? e.GetMessageString()
                       : "xbim_shape_get_location: OCCT exception");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_moved(
    XbimShapeHandle     shapeHandle,
    XbimLocationHandle  locationHandle,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_shape_moved: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }

    *outHandle = nullptr;

    if (!shapeHandle)
    {
        xbim_set_error("xbim_shape_moved: shapeHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (!locationHandle)
    {
        xbim_set_error("xbim_shape_moved: locationHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (shapeHandle->shape.IsNull())
    {
        xbim_set_error("xbim_shape_moved: shape is null");
        return XBIM_NULL_SHAPE;
    }

    try
    {
        TopoDS_Shape moved = shapeHandle->shape.Moved(locationHandle->location);

        *outHandle = xbim_shape_create_from(moved);

        if (!*outHandle)
        {
            xbim_set_error("xbim_shape_moved: allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (Standard_Failure& e)
    {
        xbim_set_error(e.GetMessageString() ? e.GetMessageString()
                       : "xbim_shape_moved: OCCT exception");
        return XBIM_ERROR;
    }
}
