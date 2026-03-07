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
#include <fstream>
#include <cstring>
#include <cstdlib>
#include <cstdint>
#include <cmath>

#include <TopoDS.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Wire.hxx>
#include <TopAbs_ShapeEnum.hxx>
#include <TopExp.hxx>
#include <TopExp_Explorer.hxx>
#include <TopTools_IndexedMapOfShape.hxx>
#include <TopTools_ShapeMapHasher.hxx>
#include <BRep_Builder.hxx>
#include <BRep_Tool.hxx>
#include <BRepGProp.hxx>
#include <BRepBndLib.hxx>
#include <GProp_GProps.hxx>
#include <Bnd_Box.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepTools.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <Poly_Triangulation.hxx>
#include <TopLoc_Location.hxx>
#include <BinTools.hxx>
#include <ShapeAnalysis.hxx>
#include <ShapeUpgrade_UnifySameDomain.hxx>
#include <gp_Trsf.hxx>
#include <gp_GTrsf.hxx>
#include <BRepBuilderAPI_Transform.hxx>
#include <BRepBuilderAPI_GTransform.hxx>

#pragma region Shape Helpers

XbimShapeHandle xbim_shape_create_from(const TopoDS_Shape& shape)
{
    XbimShape_* s = new (std::nothrow) XbimShape_();
    if (!s) return nullptr;
    s->shape = shape;
    return s;
}

#pragma endregion

#pragma region Shape Lifecycle

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

XBIM_EXPORT int XBIM_CALL xbim_shape_is_null(XbimShapeHandle handle)
{
    if (!handle)
        return XBIM_TRUE;
    return handle->shape.IsNull() || handle->shape.NbChildren() == 0 ? XBIM_TRUE : XBIM_FALSE;
}

XBIM_EXPORT int XBIM_CALL xbim_shape_is_valid(XbimShapeHandle handle)
{
    if (!handle)
        return XBIM_FALSE;
    if (handle->shape.IsNull())
        return XBIM_FALSE;

    /* Quick topology check using BRepCheck_Analyzer */
    BRepCheck_Analyzer analyzer(handle->shape, Standard_False);
    return analyzer.IsValid() ? XBIM_TRUE : XBIM_FALSE;
}

XBIM_EXPORT int XBIM_CALL xbim_shape_is_closed(XbimShapeHandle handle)
{
    if (!handle)
        return XBIM_FALSE;
    if (handle->shape.IsNull())
        return XBIM_FALSE;

    /* For solids and compsolids, they are closed by definition if valid.
     * BRep_Tool::IsClosed checks edges shared by 2 faces, which doesn't
     * always return true for valid solids in OCCT. */
    TopAbs_ShapeEnum stype = handle->shape.ShapeType();
    if (stype == TopAbs_SOLID || stype == TopAbs_COMPSOLID)
    {
        BRepCheck_Analyzer analyzer(handle->shape, Standard_False);
        return analyzer.IsValid() ? XBIM_TRUE : XBIM_FALSE;
    }

    return BRep_Tool::IsClosed(handle->shape) ? XBIM_TRUE : XBIM_FALSE;
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_reversed(
    XbimShapeHandle handle,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_shape_reversed: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!handle)
    {
        xbim_set_error("xbim_shape_reversed: handle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    if (handle->shape.IsNull())
    {
        xbim_set_error("xbim_shape_reversed: shape is null");
        return XBIM_NULL_SHAPE;
    }

    *outHandle = xbim_shape_create_from(handle->shape.Reversed());
    if (!*outHandle)
    {
        xbim_set_error("xbim_shape_reversed: memory allocation failed");
        return XBIM_ERROR;
    }

    return XBIM_OK;
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_is_same(
    XbimShapeHandle a,
    XbimShapeHandle b,
    int* outSame)
{
    xbim_clear_error();

    if (!outSame)
    {
        xbim_set_error("xbim_shape_is_same: outSame is NULL");
        return XBIM_INVALID_ARG;
    }

    if (!a || !b)
    {
        *outSame = (!a && !b) ? XBIM_TRUE : XBIM_FALSE;
        return XBIM_OK;
    }

    *outSame = a->shape.IsSame(b->shape) ? XBIM_TRUE : XBIM_FALSE;
    return XBIM_OK;
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_hash_code(
    XbimShapeHandle handle,
    int* outHash)
{
    xbim_clear_error();

    if (!outHash)
    {
        xbim_set_error("xbim_shape_hash_code: outHash is NULL");
        return XBIM_INVALID_ARG;
    }

    if (!handle || handle->shape.IsNull())
    {
        *outHash = 0;
        return XBIM_OK;
    }

    TopTools_ShapeMapHasher hasher;
    size_t h = hasher(handle->shape);
    *outHash = static_cast<int>(h & 0x7FFFFFFF);
    return XBIM_OK;
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
        box.SetGap(0.0);

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

#pragma endregion

#pragma region Topology Traversal

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

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_write_stl(
    XbimShapeHandle handle,
    const char*     filePath,
    double          deflection)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_shape_write_stl: handle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (!filePath)
    {
        xbim_set_error("xbim_shape_write_stl: filePath is NULL");
        return XBIM_INVALID_ARG;
    }
    if (handle->shape.IsNull())
    {
        xbim_set_error("xbim_shape_write_stl: shape is null");
        return XBIM_NULL_SHAPE;
    }
    if (deflection <= 0.0) deflection = 0.1;

    try
    {
        BRepMesh_IncrementalMesh mesher(handle->shape, deflection);
        if (!mesher.IsDone())
        {
            xbim_set_error("xbim_shape_write_stl: tessellation failed");
            return XBIM_ERROR;
        }

        // Count triangles
        uint32_t totalTriangles = 0;
        for (TopExp_Explorer ex(handle->shape, TopAbs_FACE); ex.More(); ex.Next())
        {
            TopLoc_Location loc;
            auto tri = BRep_Tool::Triangulation(TopoDS::Face(ex.Current()), loc);
            if (!tri.IsNull()) totalTriangles += tri->NbTriangles();
        }

        // Write binary STL
        std::ofstream ofs(filePath, std::ios::binary);
        if (!ofs)
        {
            xbim_set_error("xbim_shape_write_stl: cannot open file for writing");
            return XBIM_ERROR;
        }

        char header[80] = {};
        snprintf(header, sizeof(header), "xbim - %u triangles", totalTriangles);
        ofs.write(header, 80);
        ofs.write(reinterpret_cast<const char*>(&totalTriangles), 4);

        for (TopExp_Explorer ex(handle->shape, TopAbs_FACE); ex.More(); ex.Next())
        {
            TopoDS_Face face = TopoDS::Face(ex.Current());
            TopLoc_Location loc;
            auto tri = BRep_Tool::Triangulation(face, loc);
            if (tri.IsNull()) continue;

            gp_Trsf trsf = loc.Transformation();
            bool reversed = (face.Orientation() == TopAbs_REVERSED);

            for (int i = 1; i <= tri->NbTriangles(); ++i)
            {
                int n1, n2, n3;
                tri->Triangle(i).Get(n1, n2, n3);
                if (reversed) std::swap(n2, n3);

                gp_Pnt p1 = tri->Node(n1).Transformed(trsf);
                gp_Pnt p2 = tri->Node(n2).Transformed(trsf);
                gp_Pnt p3 = tri->Node(n3).Transformed(trsf);

                gp_Vec v1(p1, p2), v2(p1, p3);
                gp_Vec normal = v1.Crossed(v2);
                if (normal.Magnitude() > 1e-10) normal.Normalize();

                float buf[12];
                buf[0]  = (float)normal.X(); buf[1]  = (float)normal.Y(); buf[2]  = (float)normal.Z();
                buf[3]  = (float)p1.X();     buf[4]  = (float)p1.Y();     buf[5]  = (float)p1.Z();
                buf[6]  = (float)p2.X();     buf[7]  = (float)p2.Y();     buf[8]  = (float)p2.Z();
                buf[9]  = (float)p3.X();     buf[10] = (float)p3.Y();     buf[11] = (float)p3.Z();
                ofs.write(reinterpret_cast<const char*>(buf), 48);

                uint16_t attr = 0;
                ofs.write(reinterpret_cast<const char*>(&attr), 2);
            }
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_shape_write_stl: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion

#pragma region BRep Serialization

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

#pragma endregion

#pragma region Binary Serialization

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_to_binary(
    XbimShapeHandle     handle,
    int                 withTriangles,
    int                 withNormals,
    unsigned char**     outBuffer,
    int*                outSize)
{
    xbim_clear_error();

    if (!handle)
    {
        xbim_set_error("xbim_shape_to_binary: handle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (!outBuffer || !outSize)
    {
        xbim_set_error("xbim_shape_to_binary: output pointer is NULL");
        return XBIM_INVALID_ARG;
    }
    if (handle->shape.IsNull())
    {
        xbim_set_error("xbim_shape_to_binary: shape is null");
        return XBIM_NULL_SHAPE;
    }

    try
    {
        std::ostringstream oss;
        BinTools::Write(handle->shape, oss,
                        withTriangles != 0,
                        withNormals != 0,
                        BinTools_FormatVersion_VERSION_3);

        std::string data = oss.str();
        size_t len = data.size();

        unsigned char* buf = static_cast<unsigned char*>(std::malloc(len));
        if (!buf)
        {
            xbim_set_error("xbim_shape_to_binary: memory allocation failed");
            return XBIM_ERROR;
        }

        std::memcpy(buf, data.data(), len);
        *outBuffer = buf;
        *outSize = static_cast<int>(len);
        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_shape_to_binary: OCCT exception");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimShapeHandle XBIM_CALL xbim_shape_from_binary(
    const unsigned char*    buffer,
    int                     size)
{
    xbim_clear_error();

    if (!buffer || size <= 0)
    {
        xbim_set_error("xbim_shape_from_binary: buffer is NULL or size <= 0");
        return nullptr;
    }

    try
    {
        /* Create a stream from the raw buffer without copying */
        class membuf : public std::basic_streambuf<char>
        {
        public:
            membuf(const char* p, size_t n)
            {
                char* pp = const_cast<char*>(p);
                setg(pp, pp, pp + n);
            }
        };

        membuf sbuf(reinterpret_cast<const char*>(buffer), static_cast<size_t>(size));
        std::istream iss(&sbuf);

        TopoDS_Shape shape;
        BinTools::Read(shape, iss);

        if (shape.IsNull())
        {
            xbim_set_error("xbim_shape_from_binary: BinTools::Read produced a null shape");
            return nullptr;
        }

        return xbim_shape_create_from(shape);
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_shape_from_binary: OCCT exception");
        return nullptr;
    }
}

#pragma endregion

#pragma region Shape Utilities

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_unify_domain(
    XbimShapeHandle     shapeHandle,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_shape_unify_domain: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!shapeHandle)
    {
        xbim_set_error("xbim_shape_unify_domain: shapeHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (shapeHandle->shape.IsNull())
    {
        xbim_set_error("xbim_shape_unify_domain: shape is null");
        return XBIM_NULL_SHAPE;
    }

    try
    {
        ShapeUpgrade_UnifySameDomain unifier(shapeHandle->shape);
        unifier.Build();

        const TopoDS_Shape& result = unifier.Shape();
        if (result.IsNull())
        {
            xbim_set_error("xbim_shape_unify_domain: result is null");
            return XBIM_ERROR;
        }

        *outHandle = xbim_shape_create_from(result);
        if (!*outHandle)
        {
            xbim_set_error("xbim_shape_unify_domain: allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_shape_unify_domain: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_gtransform(
    XbimShapeHandle shapeHandle,
    const XbimTransformMatrix* matrix,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_shape_gtransform: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!shapeHandle)
    {
        xbim_set_error("xbim_shape_gtransform: shapeHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }
    if (shapeHandle->shape.IsNull())
    {
        xbim_set_error("xbim_shape_gtransform: shape is null");
        return XBIM_NULL_SHAPE;
    }
    if (!matrix)
    {
        xbim_set_error("xbim_shape_gtransform: matrix is NULL");
        return XBIM_INVALID_ARG;
    }

    try
    {
        gp_GTrsf trsf;
        trsf.SetValue(1, 1, matrix->m11);
        trsf.SetValue(1, 2, matrix->m12);
        trsf.SetValue(1, 3, matrix->m13);
        trsf.SetValue(1, 4, matrix->offset_x);
        trsf.SetValue(2, 1, matrix->m21);
        trsf.SetValue(2, 2, matrix->m22);
        trsf.SetValue(2, 3, matrix->m23);
        trsf.SetValue(2, 4, matrix->offset_y);
        trsf.SetValue(3, 1, matrix->m31);
        trsf.SetValue(3, 2, matrix->m32);
        trsf.SetValue(3, 3, matrix->m33);
        trsf.SetValue(3, 4, matrix->offset_z);

        if (matrix->scale_x != 0 && matrix->scale_y != 0 && matrix->scale_z != 0)
        {
            gp_GTrsf scale;
            scale.SetValue(1, 1, matrix->scale_x);
            scale.SetValue(2, 2, matrix->scale_y);
            scale.SetValue(3, 3, matrix->scale_z);
            trsf = trsf.Multiplied(scale);
        }

        /* Try to use BRepBuilderAPI_Transform (which preserves analytical surface
           geometry — planes, cylinders, etc.) instead of BRepBuilderAPI_GTransform
           (which approximates ALL surfaces as BSplines).

           A gp_GTrsf that represents a proper affine transform (rotation + translation
           + uniform scale) can be converted to a gp_Trsf. Only truly non-affine
           transforms (non-uniform scale, shearing) require BRepBuilderAPI_GTransform. */

        TopoDS_Shape transformed;

        // Extract the 3x3 rotation/scale matrix
        double r11 = trsf.Value(1, 1), r12 = trsf.Value(1, 2), r13 = trsf.Value(1, 3);
        double r21 = trsf.Value(2, 1), r22 = trsf.Value(2, 2), r23 = trsf.Value(2, 3);
        double r31 = trsf.Value(3, 1), r32 = trsf.Value(3, 2), r33 = trsf.Value(3, 3);
        double tx  = trsf.Value(1, 4), ty  = trsf.Value(2, 4), tz  = trsf.Value(3, 4);

        // Compute column lengths (scale factors along each axis)
        double sx = sqrt(r11*r11 + r21*r21 + r31*r31);
        double sy = sqrt(r12*r12 + r22*r22 + r32*r32);
        double sz = sqrt(r13*r13 + r23*r23 + r33*r33);

        // Check if the transform is affine (uniform scale + rotation + translation)
        bool isAffine = (sx > 1e-15 && sy > 1e-15 && sz > 1e-15 &&
                         fabs(sx - sy) < 1e-10 * sx &&
                         fabs(sx - sz) < 1e-10 * sx);

        if (isAffine)
        {
            // Normalize the rotation matrix
            double invS = 1.0 / sx;
            double n11 = r11 * invS, n12 = r12 * invS, n13 = r13 * invS;
            double n21 = r21 * invS, n22 = r22 * invS, n23 = r23 * invS;
            double n31 = r31 * invS, n32 = r32 * invS, n33 = r33 * invS;

            // Verify it's a proper rotation (det ≈ +1, orthogonal columns)
            double det = n11 * (n22*n33 - n23*n32)
                       - n12 * (n21*n33 - n23*n31)
                       + n13 * (n21*n32 - n22*n31);

            bool isProperRotation = (fabs(fabs(det) - 1.0) < 1e-10);

            if (isProperRotation)
            {
                // Build a proper gp_Trsf from rotation + translation + uniform scale
                gp_Trsf affineTrsf;

                if (fabs(sx - 1.0) < 1e-10)
                {
                    // Pure rotation + translation (most common IFC case)
                    // Use SetValues which handles rotation matrices directly
                    affineTrsf.SetValues(
                        n11, n12, n13, tx,
                        n21, n22, n23, ty,
                        n31, n32, n33, tz);
                }
                else
                {
                    // Rotation + translation + uniform scale
                    gp_Trsf rotTrsf;
                    rotTrsf.SetValues(
                        n11, n12, n13, tx,
                        n21, n22, n23, ty,
                        n31, n32, n33, tz);

                    gp_Trsf scaleTrsf;
                    scaleTrsf.SetScale(gp_Pnt(0.0, 0.0, 0.0), sx);

                    affineTrsf = rotTrsf.Multiplied(scaleTrsf);
                }

                BRepBuilderAPI_Transform transformer(shapeHandle->shape, affineTrsf, Standard_True);
                if (transformer.IsDone())
                {
                    transformed = transformer.Shape();
                }
                // If BRepBuilderAPI_Transform fails, fall through to GTransform
            }
        }

        // Fallback: use GTransform for non-affine transforms (non-uniform scale, shear)
        // or if the affine path failed
        if (transformed.IsNull())
        {
            BRepBuilderAPI_GTransform transformer(shapeHandle->shape, trsf, Standard_True);
            if (!transformer.IsDone())
            {
                xbim_set_error("xbim_shape_gtransform: BRepBuilderAPI_GTransform failed");
                return XBIM_ERROR;
            }
            transformed = transformer.Shape();
        }

        if (transformed.IsNull())
        {
            xbim_set_error("xbim_shape_gtransform: result shape is null");
            return XBIM_ERROR;
        }

        *outHandle = xbim_shape_create_from(transformed);
        if (!*outHandle)
        {
            xbim_set_error("xbim_shape_gtransform: allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        const char* msg = e.GetMessageString();
        xbim_set_error(msg ? msg : "xbim_shape_gtransform: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion
