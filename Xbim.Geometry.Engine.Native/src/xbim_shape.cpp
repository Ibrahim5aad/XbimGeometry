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
#include <sstream>
#include <cstring>
#include <cstdlib>

#include <TopoDS.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Wire.hxx>
#include <TopAbs_ShapeEnum.hxx>
#include <TopExp.hxx>
#include <TopExp_Explorer.hxx>
#include <TopTools_IndexedMapOfShape.hxx>
#include <BRep_Builder.hxx>
#include <BRep_Tool.hxx>
#include <BRepGProp.hxx>
#include <BRepBndLib.hxx>
#include <GProp_GProps.hxx>
#include <Bnd_Box.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepTools.hxx>
#include <ShapeAnalysis.hxx>

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

/* ── Topology traversal helpers ────────────────────────────────────────────── */

static TopAbs_ShapeEnum xbim_to_topabs(XbimShapeType st)
{
    switch (st)
    {
        case XBIM_SHAPE_VERTEX:   return TopAbs_VERTEX;
        case XBIM_SHAPE_EDGE:     return TopAbs_EDGE;
        case XBIM_SHAPE_WIRE:     return TopAbs_WIRE;
        case XBIM_SHAPE_FACE:     return TopAbs_FACE;
        case XBIM_SHAPE_SHELL:    return TopAbs_SHELL;
        case XBIM_SHAPE_SOLID:    return TopAbs_SOLID;
        case XBIM_SHAPE_COMPOUND: return TopAbs_COMPOUND;
        default:                  return TopAbs_SHAPE;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_count_subshapes(
    XbimShapeHandle handle,
    XbimShapeType   subType,
    int*            outCount)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_shape_count_subshapes: handle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (!outCount)
    {
        xbim_set_error("xbim_shape_count_subshapes: outCount is NULL");
        return XBIM_INVALID_ARG;
    }
    if (handle->shape.IsNull())
    {
        xbim_set_error("xbim_shape_count_subshapes: shape is null");
        return XBIM_NULL_SHAPE;
    }

    try
    {
        TopAbs_ShapeEnum topoType = xbim_to_topabs(subType);
        TopTools_IndexedMapOfShape map;
        TopExp::MapShapes(handle->shape, topoType, map);

        *outCount = map.Extent();
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_shape_count_subshapes: OCCT exception");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_get_subshapes(
    XbimShapeHandle     handle,
    XbimShapeType       subType,
    XbimShapeHandle*    outHandles,
    int*                count)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_shape_get_subshapes: handle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (!outHandles || !count)
    {
        xbim_set_error("xbim_shape_get_subshapes: outHandles or count is NULL");
        return XBIM_INVALID_ARG;
    }
    if (handle->shape.IsNull())
    {
        xbim_set_error("xbim_shape_get_subshapes: shape is null");
        return XBIM_NULL_SHAPE;
    }

    try
    {
        TopAbs_ShapeEnum topoType = xbim_to_topabs(subType);
        TopTools_IndexedMapOfShape map;
        TopExp::MapShapes(handle->shape, topoType, map);

        int capacity = *count;
        int numShapes = map.Extent();

        if (numShapes > capacity)
        {
            xbim_set_error("xbim_shape_get_subshapes: array capacity too small");
            *count = 0;
            return XBIM_INVALID_ARG;
        }

        for (int i = 1; i <= numShapes; ++i) /* OCCT maps are 1-based */
        {
            XbimShapeHandle sub = xbim_shape_create_from(map.FindKey(i));
            if (!sub)
            {
                xbim_set_error("xbim_shape_get_subshapes: allocation failed");
                for (int j = 0; j < i - 1; ++j)
                {
                    delete outHandles[j];
                    outHandles[j] = nullptr;
                }
                *count = 0;
                return XBIM_ERROR;
            }
            outHandles[i - 1] = sub;
        }

        *count = numShapes;
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_shape_get_subshapes: OCCT exception");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_face_outer_wire(
    XbimShapeHandle     faceHandle,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();

    if (!faceHandle)
    {
        xbim_set_error("xbim_face_outer_wire: faceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (!outHandle)
    {
        xbim_set_error("xbim_face_outer_wire: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    if (faceHandle->shape.IsNull())
    {
        xbim_set_error("xbim_face_outer_wire: face is null");
        return XBIM_NULL_SHAPE;
    }
    if (faceHandle->shape.ShapeType() != TopAbs_FACE)
    {
        xbim_set_error("xbim_face_outer_wire: shape is not a face");
        return XBIM_INVALID_ARG;
    }

    try
    {
        const TopoDS_Face& face = TopoDS::Face(faceHandle->shape);
        TopoDS_Wire outerWire = ShapeAnalysis::OuterWire(face);

        if (outerWire.IsNull())
        {
            xbim_set_error("xbim_face_outer_wire: outer wire is null");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(static_cast<const TopoDS_Shape&>(outerWire));
        if (!*outHandle)
        {
            xbim_set_error("xbim_face_outer_wire: allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_face_outer_wire: OCCT exception");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_face_inner_wires(
    XbimShapeHandle     faceHandle,
    XbimShapeHandle*    outHandles,
    int*                count)
{
    xbim_clear_error();

    if (!faceHandle)
    {
        xbim_set_error("xbim_face_inner_wires: faceHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (!outHandles || !count)
    {
        xbim_set_error("xbim_face_inner_wires: outHandles or count is NULL");
        return XBIM_INVALID_ARG;
    }
    if (faceHandle->shape.IsNull())
    {
        xbim_set_error("xbim_face_inner_wires: face is null");
        return XBIM_NULL_SHAPE;
    }
    if (faceHandle->shape.ShapeType() != TopAbs_FACE)
    {
        xbim_set_error("xbim_face_inner_wires: shape is not a face");
        return XBIM_INVALID_ARG;
    }

    try
    {
        const TopoDS_Face& face = TopoDS::Face(faceHandle->shape);
        TopoDS_Wire outerWire = ShapeAnalysis::OuterWire(face);

        int capacity = *count;
        int idx = 0;

        for (TopExp_Explorer ex(faceHandle->shape, TopAbs_WIRE); ex.More(); ex.Next())
        {
            /* Skip the outer wire */
            if (ex.Current().IsSame(static_cast<const TopoDS_Shape&>(outerWire)))
                continue;

            if (idx >= capacity)
            {
                xbim_set_error("xbim_face_inner_wires: array capacity too small");
                for (int i = 0; i < idx; ++i)
                {
                    delete outHandles[i];
                    outHandles[i] = nullptr;
                }
                *count = 0;
                return XBIM_INVALID_ARG;
            }

            XbimShapeHandle sub = xbim_shape_create_from(ex.Current());
            if (!sub)
            {
                xbim_set_error("xbim_face_inner_wires: allocation failed");
                for (int i = 0; i < idx; ++i)
                {
                    delete outHandles[i];
                    outHandles[i] = nullptr;
                }
                *count = 0;
                return XBIM_ERROR;
            }
            outHandles[idx++] = sub;
        }

        *count = idx;
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_face_inner_wires: OCCT exception");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_write_brep(
    XbimShapeHandle handle,
    const char*     filePath)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_shape_write_brep: handle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (!filePath)
    {
        xbim_set_error("xbim_shape_write_brep: filePath is NULL");
        return XBIM_INVALID_ARG;
    }
    if (handle->shape.IsNull())
    {
        xbim_set_error("xbim_shape_write_brep: shape is null");
        return XBIM_NULL_SHAPE;
    }

    try
    {
        Standard_Boolean ok = BRepTools::Write(handle->shape, filePath);
        if (!ok)
        {
            xbim_set_error("xbim_shape_write_brep: BRepTools::Write failed");
            return XBIM_ERROR;
        }
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_shape_write_brep: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── BRep string serialization ──────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_to_brep_string(
    XbimShapeHandle handle,
    char**          outBrepStr,
    int*            outStrLen)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_shape_to_brep_string: handle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (!outBrepStr || !outStrLen)
    {
        xbim_set_error("xbim_shape_to_brep_string: output pointer is NULL");
        return XBIM_INVALID_ARG;
    }
    if (handle->shape.IsNull())
    {
        xbim_set_error("xbim_shape_to_brep_string: shape is null");
        return XBIM_NULL_SHAPE;
    }

    try
    {
        std::ostringstream oss;
        BRepTools::Write(handle->shape, oss);

        std::string str = oss.str();
        size_t len = str.size();

        char* buf = static_cast<char*>(std::malloc(len + 1));
        if (!buf)
        {
            xbim_set_error("xbim_shape_to_brep_string: memory allocation failed");
            return XBIM_ERROR;
        }

        std::memcpy(buf, str.c_str(), len + 1);
        *outBrepStr = buf;
        *outStrLen = static_cast<int>(len);
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_shape_to_brep_string: OCCT exception");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimShapeHandle XBIM_CALL xbim_shape_from_brep_string(
    const char* brepStr,
    int         strLen)
{
    xbim_clear_error();

    if (!brepStr)
    {
        xbim_set_error("xbim_shape_from_brep_string: brepStr is NULL");
        return nullptr;
    }

    try
    {
        size_t len = (strLen >= 0)
            ? static_cast<size_t>(strLen)
            : std::strlen(brepStr);

        std::string data(brepStr, len);
        std::istringstream iss(data);

        TopoDS_Shape shape;
        BRep_Builder builder;
        BRepTools::Read(shape, iss, builder);

        if (shape.IsNull())
        {
            xbim_set_error("xbim_shape_from_brep_string: BRepTools::Read failed");
            return nullptr;
        }

        return xbim_shape_create_from(shape);
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_shape_from_brep_string: OCCT exception");
        return nullptr;
    }
}

XBIM_EXPORT void XBIM_CALL xbim_string_free(char* str)
{
    std::free(str);
}
