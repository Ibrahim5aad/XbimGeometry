/*
 * xbim_shape.cpp
 *
 * Implements the shape handle lifecycle and query functions.
 * Each XbimShapeHandle wraps a heap-allocated TopoDS_Shape with
 * operations for type query, validity check, closure check,
 * bounding box, volume, and surface area.
 */

#include "xbim_shape.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <new>

#include <TopoDS.hxx>
#include <TopAbs_ShapeEnum.hxx>
#include <BRep_Tool.hxx>
#include <BRepGProp.hxx>
#include <BRepBndLib.hxx>
#include <GProp_GProps.hxx>
#include <Bnd_Box.hxx>
#include <BRepCheck_Analyzer.hxx>

/* ── Internal helper ──────────────────────────────────────────────────────── */

XbimShapeHandle xbim_shape_create_from(const TopoDS_Shape& shape)
{
    XbimShape_* s = new (std::nothrow) XbimShape_();
    if (!s) return nullptr;
    s->shape = shape;
    return s;
}

/* ── Public API ───────────────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_destroy(XbimShapeHandle handle)
{
    xbim_clear_error();

    if (!handle)
        return XBIM_OK; /* destroying null is a safe no-op */

    delete handle;
    return XBIM_OK;
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_type(
    XbimShapeHandle handle,
    XbimShapeType*  outType)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_shape_type: handle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (!outType)
    {
        xbim_set_error("xbim_shape_type: outType is NULL");
        return XBIM_INVALID_ARG;
    }

    if (handle->shape.IsNull())
    {
        xbim_set_error("xbim_shape_type: shape is null");
        return XBIM_NULL_SHAPE;
    }

    TopAbs_ShapeEnum occtType = handle->shape.ShapeType();

    switch (occtType)
    {
        case TopAbs_VERTEX:   *outType = XBIM_SHAPE_VERTEX;   break;
        case TopAbs_EDGE:     *outType = XBIM_SHAPE_EDGE;     break;
        case TopAbs_WIRE:     *outType = XBIM_SHAPE_WIRE;     break;
        case TopAbs_FACE:     *outType = XBIM_SHAPE_FACE;     break;
        case TopAbs_SHELL:    *outType = XBIM_SHAPE_SHELL;    break;
        case TopAbs_SOLID:    *outType = XBIM_SHAPE_SOLID;    break;
        case TopAbs_COMPOUND:
        case TopAbs_COMPSOLID:
            *outType = XBIM_SHAPE_COMPOUND;
            break;
        default:
            *outType = XBIM_SHAPE_COMPOUND;
            break;
    }

    return XBIM_OK;
}

XBIM_EXPORT int XBIM_CALL xbim_shape_is_valid(XbimShapeHandle handle)
{
    if (!handle)
        return 0;
    if (handle->shape.IsNull())
        return 0;

    /* Quick topology check using BRepCheck_Analyzer */
    BRepCheck_Analyzer analyzer(handle->shape, Standard_False);
    return analyzer.IsValid() ? 1 : 0;
}

XBIM_EXPORT int XBIM_CALL xbim_shape_is_closed(XbimShapeHandle handle)
{
    if (!handle)
        return 0;
    if (handle->shape.IsNull())
        return 0;

    /* For solids and compsolids, they are closed by definition if valid.
     * BRep_Tool::IsClosed checks edges shared by 2 faces, which doesn't
     * always return true for valid solids in OCCT. */
    TopAbs_ShapeEnum stype = handle->shape.ShapeType();
    if (stype == TopAbs_SOLID || stype == TopAbs_COMPSOLID)
    {
        BRepCheck_Analyzer analyzer(handle->shape, Standard_False);
        return analyzer.IsValid() ? 1 : 0;
    }

    return BRep_Tool::IsClosed(handle->shape) ? 1 : 0;
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_bounding_box(
    XbimShapeHandle handle,
    double* minX, double* minY, double* minZ,
    double* maxX, double* maxY, double* maxZ)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_shape_bounding_box: handle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (!minX || !minY || !minZ || !maxX || !maxY || !maxZ)
    {
        xbim_set_error("xbim_shape_bounding_box: output pointer is NULL");
        return XBIM_INVALID_ARG;
    }
    if (handle->shape.IsNull())
    {
        xbim_set_error("xbim_shape_bounding_box: shape is null");
        return XBIM_NULL_SHAPE;
    }

    try
    {
        Bnd_Box box;
        BRepBndLib::Add(handle->shape, box);

        if (box.IsVoid())
        {
            xbim_set_error("xbim_shape_bounding_box: bounding box is void");
            return XBIM_ERROR;
        }

        box.Get(*minX, *minY, *minZ, *maxX, *maxY, *maxZ);
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_shape_bounding_box: OCCT exception");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_volume(
    XbimShapeHandle handle,
    double*         outVolume)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_shape_volume: handle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (!outVolume)
    {
        xbim_set_error("xbim_shape_volume: outVolume is NULL");
        return XBIM_INVALID_ARG;
    }
    if (handle->shape.IsNull())
    {
        xbim_set_error("xbim_shape_volume: shape is null");
        return XBIM_NULL_SHAPE;
    }

    try
    {
        GProp_GProps props;
        BRepGProp::VolumeProperties(handle->shape, props);
        *outVolume = props.Mass();
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_shape_volume: OCCT exception");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_surface_area(
    XbimShapeHandle handle,
    double*         outArea)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_shape_surface_area: handle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (!outArea)
    {
        xbim_set_error("xbim_shape_surface_area: outArea is NULL");
        return XBIM_INVALID_ARG;
    }
    if (handle->shape.IsNull())
    {
        xbim_set_error("xbim_shape_surface_area: shape is null");
        return XBIM_NULL_SHAPE;
    }

    try
    {
        GProp_GProps props;
        BRepGProp::SurfaceProperties(handle->shape, props);
        *outArea = props.Mass();
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_shape_surface_area: OCCT exception");
        return XBIM_ERROR;
    }
}
