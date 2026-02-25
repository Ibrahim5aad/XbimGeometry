/*
 * xbim_mesh.cpp
 *
 * Implements WexBim mesh creation 
 */

#include "xbim_mesh.h"
#include "xbim_shape.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <cstring>
#include <cmath>

#include <BRep_Tool.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRepTools.hxx>
#include <Geom_Plane.hxx>
#include <Geom_Line.hxx>
#include <Geom_TrimmedCurve.hxx>
#include <TopoDS.hxx>
#include <Standard_Failure.hxx>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

/* ══════════════════════════════════════════════════════════════════════════
 * PackedNormal
 * ══════════════════════════════════════════════════════════════════════════ */

static const double PACK_SIZE = 252.0;

PackedNormal PackedNormal::FromDirection(const gp_Dir& vec)
{
    const double packTolerance = std::tan(1.0 / PACK_SIZE);
    static const double halfPI = M_PI / 2.0;
    static const double piPlusHalfPI = M_PI + halfPI;
    static const double twoPI = M_PI * 2.0;

    // Singular points: Y-aligned normals
    if (std::abs(1.0 - vec.Y()) < packTolerance)
        return PackedNormal(0, 0);
    if (std::abs(vec.Y() + 1.0) < packTolerance)
        return PackedNormal(static_cast<unsigned char>(PACK_SIZE),
                            static_cast<unsigned char>(PACK_SIZE));

    double lat, lon;

    // Axis-aligned special cases
    if (std::abs(vec.Z() - 1.0) < packTolerance)
    {
        lon = 0.0;
        lat = halfPI;
    }
    else if (std::abs(vec.Z() + 1.0) < packTolerance)
    {
        lon = M_PI;
        lat = halfPI;
    }
    else if (std::abs(vec.X() - 1.0) < packTolerance)
    {
        lon = halfPI;
        lat = halfPI;
    }
    else if (std::abs(vec.X() + 1.0) < packTolerance)
    {
        lon = piPlusHalfPI;
        lat = halfPI;
    }
    else
    {
        lon = std::atan2(vec.X(), vec.Z());
        lat = std::acos(vec.Y());
    }

    lon = lon / twoPI;
    lat = lat / M_PI;

    return PackedNormal(static_cast<unsigned char>(lon * PACK_SIZE),
                        static_cast<unsigned char>(lat * PACK_SIZE));
}

/* ══════════════════════════════════════════════════════════════════════════
 * MeshPointInspector
 * ══════════════════════════════════════════════════════════════════════════ */

NCollection_CellFilter_Action MeshPointInspector::Inspect(Standard_Integer theID)
{
    const gp_XYZ& aXYZ = points_.Value(theID);
    Standard_Real dx = current_.X() - aXYZ.X();
    Standard_Real dy = current_.Y() - aXYZ.Y();
    Standard_Real dz = current_.Z() - aXYZ.Z();
    Standard_Real sqDist = dx * dx + dy * dy + dz * dz;

    if (sqDist < sqTolerance_)
        resultIndex_ = theID;

    return CellFilter_Keep;
}

/* ══════════════════════════════════════════════════════════════════════════
 * FaceMeshIterator
 * ══════════════════════════════════════════════════════════════════════════ */

FaceMeshIterator::FaceMeshIterator(const TopoDS_Shape& shape, bool checkEdges)
    : checkEdges_(checkEdges),
      slTool_(1, 1e-12),
      isMirrored_(false)
{
    HasCurves = false;
    faceIter_.Init(shape, TopAbs_FACE);
    Next();
}

void FaceMeshIterator::Next()
{
    for (; faceIter_.More(); faceIter_.Next())
    {
        face_ = TopoDS::Face(faceIter_.Current());
        polyTriang_ = BRep_Tool::Triangulation(face_, faceLocation_);
        trsf_ = faceLocation_.Transformation();

        if (polyTriang_.IsNull() || polyTriang_->NbTriangles() == 0)
        {
            polyTriang_.Nullify();
            face_.Nullify();
            normals_.Clear();
            isPlanar_ = false;
            continue;
        }

        initFace();
        faceIter_.Next();
        return;
    }

    polyTriang_.Nullify();
    face_.Nullify();
    normals_.Clear();
    isPlanar_ = false;
}

void FaceMeshIterator::initFace()
{
    isMirrored_ = trsf_.VectorialPart().Determinant() < 0.0;
    normals_.Clear();
    isPlanar_ = false;

    if (checkEdges_)
        HasCurves = hasCurves();

    if (polyTriang_->HasUVNodes())
    {
        TopoDS_Face faceFwd = TopoDS::Face(face_.Oriented(TopAbs_FORWARD));
        faceFwd.Location(TopLoc_Location());
        TopLoc_Location aLoc;
        Handle(Geom_Surface) surface = BRep_Tool::Surface(faceFwd, aLoc);

        if (!surface.IsNull())
        {
            Handle(Geom_Plane) hPlane = Handle(Geom_Plane)::DownCast(surface);
            isPlanar_ = !hPlane.IsNull();
            faceAdaptor_.Initialize(faceFwd, false);
            slTool_.SetSurface(faceAdaptor_);

            int numNodes = isPlanar_ ? 1 : polyTriang_->NbNodes();
            for (int i = 1; i <= numNodes; i++)
            {
                gp_Dir aNormal(gp::DZ());
                if (polyTriang_->HasUVNodes())
                {
                    const gp_XY uv = polyTriang_->UVNode(i).XY();
                    slTool_.SetParameters(uv.X(), uv.Y());
                    if (slTool_.IsNormalDefined())
                        aNormal = slTool_.Normal();
                }
                normals_.Append(aNormal);
            }
        }
    }
}

gp_Dir FaceMeshIterator::normal(Standard_Integer theNode)
{
    if (isPlanar_)
        return normals_(0);
    else
        return normals_.Value(theNode - 1);
}

bool FaceMeshIterator::hasCurves()
{
    for (TopExp_Explorer edgeExp(face_, TopAbs_EDGE); edgeExp.More(); edgeExp.Next())
    {
        Standard_Real start, end;
        Handle(Geom_Curve) c3d = BRep_Tool::Curve(
            TopoDS::Edge(edgeExp.Current()), start, end);

        if (!c3d.IsNull())
        {
            if (c3d->DynamicType() == STANDARD_TYPE(Geom_Line))
                continue;

            if (c3d->DynamicType() == STANDARD_TYPE(Geom_TrimmedCurve))
            {
                Handle(Geom_TrimmedCurve) tc =
                    Handle(Geom_TrimmedCurve)::DownCast(c3d);
                while (tc->BasisCurve()->DynamicType() == STANDARD_TYPE(Geom_TrimmedCurve))
                    tc = Handle(Geom_TrimmedCurve)::DownCast(tc->BasisCurve());

                if (tc->BasisCurve()->DynamicType() == STANDARD_TYPE(Geom_Line))
                    continue;
            }
            return true;
        }
    }
    return false;
}

/* ══════════════════════════════════════════════════════════════════════════
 * WexBimMesh
 * ══════════════════════════════════════════════════════════════════════════ */

WexBimMesh::WexBimMesh(double tolerance, double scale)
    : tolerance_(tolerance),
      scale_(scale),
      pointInspector_(tolerance * scale),
      pointFilter_(tolerance * scale)
{
}

int WexBimMesh::TriangleCount() const
{
    int count = 0;
    for (const auto& faceIndices : indicesPerFace_)
        count += faceIndices.Length();
    return count;
}

int WexBimMesh::addPoint(gp_XYZ point)
{
    pointInspector_.SetCurrent(point);
    pointFilter_.Inspect(point, pointInspector_);
    int idx = pointInspector_.ResInd();
    if (idx >= 0)
        return idx;

    int newIdx = pointInspector_.Add(point);
    pointFilter_.Add(newIdx, point);
    return newIdx;
}

void WexBimMesh::saveNodes(const FaceMeshIterator& faceIter, std::vector<int>& nodeIndexes)
{
    const Standard_Integer upper = faceIter.NodeUpper();
    for (Standard_Integer i = faceIter.NodeLower(); i <= upper; ++i)
    {
        gp_XYZ node = faceIter.NodeTransformed(i).XYZ();
        node.Multiply(scale_);
        BndBox.Add(BVH_Vec3d(node.X(), node.Y(), node.Z()));
        nodeIndexes.push_back(addPoint(node));
    }
}

void WexBimMesh::SaveIndicesAndNormals(FaceMeshIterator& faceIter)
{
    const Standard_Integer elemLower = faceIter.ElemLower();
    const Standard_Integer elemUpper = faceIter.ElemUpper();

    NCollection_Vector<Vec3Int> triangleIndices;
    bool donePlanar = false;
    bool isPlanar = faceIter.IsPlanar();
    std::vector<PackedNormal> normals;

    std::vector<int> nodeIndexes;
    saveNodes(faceIter, nodeIndexes);

    for (Standard_Integer i = elemLower; i <= elemUpper; ++i)
    {
        Poly_Triangle tri = faceIter.TriangleOriented(i);
        int idx1 = nodeIndexes[tri(1) - elemLower];
        int idx2 = nodeIndexes[tri(2) - elemLower];
        int idx3 = nodeIndexes[tri(3) - elemLower];
        triangleIndices.Append(Vec3Int(idx1, idx2, idx3));

        if (donePlanar)
            continue;
        if (isPlanar)
            donePlanar = true;

        gp_Dir n = faceIter.NormalTransformed(tri(1));
        normals.push_back(PackedNormal::FromDirection(n));

        if (!isPlanar)
        {
            n = faceIter.NormalTransformed(tri(2));
            normals.push_back(PackedNormal::FromDirection(n));
            n = faceIter.NormalTransformed(tri(3));
            normals.push_back(PackedNormal::FromDirection(n));
        }
    }

    indicesPerFace_.push_back(std::move(triangleIndices));
    normalsPerFace_.push_back(std::move(normals));
}

#pragma region WexBim Serialization

template<typename T>
static void writeValue(std::vector<unsigned char>& buf, const T& val)
{
    const unsigned char* p = reinterpret_cast<const unsigned char*>(&val);
    buf.insert(buf.end(), p, p + sizeof(T));
}

void WexBimMesh::writeTriangleIndices(std::vector<unsigned char>& buf,
                                       const Vec3Int& tri,
                                       unsigned int maxVertices) const
{
    if (maxVertices <= 0xFF)
    {
        unsigned char x = static_cast<unsigned char>(tri.x());
        unsigned char y = static_cast<unsigned char>(tri.y());
        unsigned char z = static_cast<unsigned char>(tri.z());
        buf.push_back(x);
        buf.push_back(y);
        buf.push_back(z);
    }
    else if (maxVertices <= 0xFFFF)
    {
        writeValue<uint16_t>(buf, static_cast<uint16_t>(tri.x()));
        writeValue<uint16_t>(buf, static_cast<uint16_t>(tri.y()));
        writeValue<uint16_t>(buf, static_cast<uint16_t>(tri.z()));
    }
    else
    {
        writeValue<int>(buf, tri.x());
        writeValue<int>(buf, tri.y());
        writeValue<int>(buf, tri.z());
    }
}

void WexBimMesh::writeTriangleIndicesWithNormals(std::vector<unsigned char>& buf,
                                                  const Vec3Int& tri,
                                                  const PackedNormal& a,
                                                  const PackedNormal& b,
                                                  const PackedNormal& c,
                                                  unsigned int maxVertices) const
{
    if (maxVertices <= 0xFF)
    {
        buf.push_back(static_cast<unsigned char>(tri.x()));
        buf.push_back(a.bytes[0]); buf.push_back(a.bytes[1]);
        buf.push_back(static_cast<unsigned char>(tri.y()));
        buf.push_back(b.bytes[0]); buf.push_back(b.bytes[1]);
        buf.push_back(static_cast<unsigned char>(tri.z()));
        buf.push_back(c.bytes[0]); buf.push_back(c.bytes[1]);
    }
    else if (maxVertices <= 0xFFFF)
    {
        writeValue<uint16_t>(buf, static_cast<uint16_t>(tri.x()));
        buf.push_back(a.bytes[0]); buf.push_back(a.bytes[1]);
        writeValue<uint16_t>(buf, static_cast<uint16_t>(tri.y()));
        buf.push_back(b.bytes[0]); buf.push_back(b.bytes[1]);
        writeValue<uint16_t>(buf, static_cast<uint16_t>(tri.z()));
        buf.push_back(c.bytes[0]); buf.push_back(c.bytes[1]);
    }
    else
    {
        writeValue<int>(buf, tri.x());
        buf.push_back(a.bytes[0]); buf.push_back(a.bytes[1]);
        writeValue<int>(buf, tri.y());
        buf.push_back(b.bytes[0]); buf.push_back(b.bytes[1]);
        writeValue<int>(buf, tri.z());
        buf.push_back(c.bytes[0]); buf.push_back(c.bytes[1]);
    }
}

std::vector<unsigned char> WexBimMesh::Serialize() const
{
    std::vector<unsigned char> buf;

    // Reserve rough estimate
    buf.reserve(1 + 4 + 4 + VertexCount() * 12 + 4 + TriangleCount() * 12);

    // Version
    writeValue<unsigned char>(buf, VERSION);

    // Vertex count and triangle count
    unsigned int numVertices = static_cast<unsigned int>(VertexCount());
    unsigned int numTriangles = static_cast<unsigned int>(TriangleCount());
    writeValue<unsigned int>(buf, numVertices);
    writeValue<unsigned int>(buf, numTriangles);

    // Vertices as float32 XYZ
    for (int i = 0; i < pointInspector_.points_.Length(); ++i)
    {
        const gp_XYZ& v = pointInspector_.points_.Value(i);
        float fx = static_cast<float>(v.X());
        float fy = static_cast<float>(v.Y());
        float fz = static_cast<float>(v.Z());
        writeValue<float>(buf, fx);
        writeValue<float>(buf, fy);
        writeValue<float>(buf, fz);
    }

    // Face count
    int faceCount = FaceCount();
    writeValue<int>(buf, faceCount);

    // Per-face data
    for (int fi = 0; fi < faceCount; ++fi)
    {
        const auto& faceIndices = indicesPerFace_[fi];
        const auto& faceNormals = normalsPerFace_[fi];
        bool isPlanar = faceNormals.size() == 1;
        int numTrisForFace = faceIndices.Length();

        if (isPlanar)
        {
            writeValue<int>(buf, numTrisForFace);
            writeValue<PackedNormal>(buf, faceNormals.front());
        }
        else
        {
            int negCount = -numTrisForFace;
            writeValue<int>(buf, negCount);
        }

        auto normalIt = faceNormals.cbegin();
        for (auto triIt = faceIndices.cbegin(); triIt != faceIndices.cend(); ++triIt)
        {
            if (isPlanar)
            {
                writeTriangleIndices(buf, *triIt, numVertices);
            }
            else
            {
                const PackedNormal& a = *normalIt; ++normalIt;
                const PackedNormal& b = *normalIt; ++normalIt;
                const PackedNormal& c = *normalIt; ++normalIt;
                writeTriangleIndicesWithNormals(buf, *triIt, a, b, c, numVertices);
            }
        }
    }

    return buf;
}

/* ══════════════════════════════════════════════════════════════════════════
 * Public C API
 * ══════════════════════════════════════════════════════════════════════════ */

XBIM_EXPORT XbimResult XBIM_CALL xbim_mesh_create_wexbim(
    XbimContextHandle   ctx,
    XbimShapeHandle     shapeHandle,
    double              tolerance,
    double              linearDeflection,
    double              angularDeflection,
    double              scale,
    int                 checkEdges,
    unsigned char**     outBuffer,
    int*                outBufferSize,
    int*                outHasCurves,
    double*             outMinX, double* outMinY, double* outMinZ,
    double*             outMaxX, double* outMaxY, double* outMaxZ)
{
    xbim_clear_error();

    if (!ctx)
    {
        xbim_set_error("xbim_mesh_create_wexbim: null context handle");
        return XBIM_INVALID_HANDLE;
    }
    if (!shapeHandle)
    {
        xbim_set_error("xbim_mesh_create_wexbim: null shape handle");
        return XBIM_INVALID_HANDLE;
    }
    if (!outBuffer || !outBufferSize)
    {
        xbim_set_error("xbim_mesh_create_wexbim: null output parameter");
        return XBIM_INVALID_ARG;
    }

    const TopoDS_Shape& shape = shapeHandle->shape;
    if (shape.IsNull())
    {
        xbim_set_error("xbim_mesh_create_wexbim: shape is null");
        return XBIM_NULL_SHAPE;
    }

    try
    {
        WexBimMesh mesh(tolerance, scale);

        // Clean existing triangulation and re-triangulate
        BRepTools::Clean(shape);
        BRepMesh_IncrementalMesh incrementalMesh(
            shape, linearDeflection, Standard_False, angularDeflection);

        // Iterate faces and collect mesh data
        for (FaceMeshIterator faceIter(shape, checkEdges != 0);
             faceIter.More(); faceIter.Next())
        {
            if (faceIter.IsEmptyMesh())
                continue;
            if (!mesh.HasCurves)
                mesh.HasCurves = faceIter.HasCurves;
            mesh.SaveIndicesAndNormals(faceIter);
        }

        // Clean triangulation after serialization to save memory
        BRepTools::Clean(shape);

        // Serialize to WexBim binary format
        std::vector<unsigned char> data = mesh.Serialize();

        // Allocate native buffer and copy
        *outBufferSize = static_cast<int>(data.size());
        *outBuffer = static_cast<unsigned char*>(std::malloc(data.size()));
        if (!*outBuffer)
        {
            xbim_set_error("xbim_mesh_create_wexbim: memory allocation failed");
            return XBIM_ERROR;
        }
        std::memcpy(*outBuffer, data.data(), data.size());

        if (outHasCurves)
            *outHasCurves = mesh.HasCurves ? 1 : 0;

        // Return bounding box computed during meshing (avoids re-triangulation)
        if (mesh.BndBox.IsValid())
        {
            if (outMinX) *outMinX = mesh.BndBox.CornerMin().x();
            if (outMinY) *outMinY = mesh.BndBox.CornerMin().y();
            if (outMinZ) *outMinZ = mesh.BndBox.CornerMin().z();
            if (outMaxX) *outMaxX = mesh.BndBox.CornerMax().x();
            if (outMaxY) *outMaxY = mesh.BndBox.CornerMax().y();
            if (outMaxZ) *outMaxZ = mesh.BndBox.CornerMax().z();
        }
        else
        {
            if (outMinX) *outMinX = 0.0;
            if (outMinY) *outMinY = 0.0;
            if (outMinZ) *outMinZ = 0.0;
            if (outMaxX) *outMaxX = 0.0;
            if (outMaxY) *outMaxY = 0.0;
            if (outMaxZ) *outMaxZ = 0.0;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        std::string msg = "xbim_mesh_create_wexbim: OCCT error - ";
        msg += e.GetMessageString() ? e.GetMessageString() : "unknown";
        xbim_set_error(msg.c_str());
        xbim_log_message(ctx, XBIM_LOG_ERROR, msg.c_str());
        return XBIM_ERROR;
    }
    catch (const std::exception& e)
    {
        std::string msg = "xbim_mesh_create_wexbim: C++ exception - ";
        msg += e.what();
        xbim_set_error(msg.c_str());
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_mesh_get_bounding_box(
    XbimContextHandle   ctx,
    XbimShapeHandle     shapeHandle,
    double              tolerance,
    double              linearDeflection,
    double              angularDeflection,
    double              scale,
    double*             outMinX, double* outMinY, double* outMinZ,
    double*             outMaxX, double* outMaxY, double* outMaxZ)
{
    xbim_clear_error();

    if (!ctx)
    {
        xbim_set_error("xbim_mesh_get_bounding_box: null context handle");
        return XBIM_INVALID_HANDLE;
    }
    if (!shapeHandle)
    {
        xbim_set_error("xbim_mesh_get_bounding_box: null shape handle");
        return XBIM_INVALID_HANDLE;
    }

    const TopoDS_Shape& shape = shapeHandle->shape;
    if (shape.IsNull())
    {
        xbim_set_error("xbim_mesh_get_bounding_box: shape is null");
        return XBIM_NULL_SHAPE;
    }

    try
    {
        WexBimMesh mesh(tolerance, scale);

        BRepTools::Clean(shape);
        BRepMesh_IncrementalMesh incrementalMesh(
            shape, linearDeflection, Standard_False, angularDeflection);

        for (FaceMeshIterator faceIter(shape, false);
             faceIter.More(); faceIter.Next())
        {
            if (faceIter.IsEmptyMesh())
                continue;
            mesh.SaveIndicesAndNormals(faceIter);
        }

        BRepTools::Clean(shape);

        if (mesh.BndBox.IsValid())
        {
            if (outMinX) *outMinX = mesh.BndBox.CornerMin().x();
            if (outMinY) *outMinY = mesh.BndBox.CornerMin().y();
            if (outMinZ) *outMinZ = mesh.BndBox.CornerMin().z();
            if (outMaxX) *outMaxX = mesh.BndBox.CornerMax().x();
            if (outMaxY) *outMaxY = mesh.BndBox.CornerMax().y();
            if (outMaxZ) *outMaxZ = mesh.BndBox.CornerMax().z();
        }
        else
        {
            if (outMinX) *outMinX = 0.0;
            if (outMinY) *outMinY = 0.0;
            if (outMinZ) *outMinZ = 0.0;
            if (outMaxX) *outMaxX = 0.0;
            if (outMaxY) *outMaxY = 0.0;
            if (outMaxZ) *outMaxZ = 0.0;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        std::string msg = "xbim_mesh_get_bounding_box: OCCT error - ";
        msg += e.GetMessageString() ? e.GetMessageString() : "unknown";
        xbim_set_error(msg.c_str());
        return XBIM_ERROR;
    }
    catch (const std::exception& e)
    {
        std::string msg = "xbim_mesh_get_bounding_box: C++ exception - ";
        msg += e.what();
        xbim_set_error(msg.c_str());
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_triangulate(
    XbimShapeHandle     shapeHandle,
    double              linearDeflection,
    double              angularDeflection,
    int                 relative)
{
    xbim_clear_error();

    if (!shapeHandle)
    {
        xbim_set_error("xbim_shape_triangulate: null shape handle");
        return XBIM_INVALID_HANDLE;
    }

    const TopoDS_Shape& shape = shapeHandle->shape;
    if (shape.IsNull())
    {
        xbim_set_error("xbim_shape_triangulate: shape is null");
        return XBIM_NULL_SHAPE;
    }

    try
    {
        BRepMesh_IncrementalMesh mesh(
            shape, linearDeflection,
            relative != 0 ? Standard_True : Standard_False,
            angularDeflection);

        if (!mesh.IsDone())
        {
            xbim_set_error("xbim_shape_triangulate: meshing failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        std::string msg = "xbim_shape_triangulate: OCCT error - ";
        msg += e.GetMessageString() ? e.GetMessageString() : "unknown";
        xbim_set_error(msg.c_str());
        return XBIM_ERROR;
    }
    catch (const std::exception& e)
    {
        std::string msg = "xbim_shape_triangulate: C++ exception - ";
        msg += e.what();
        xbim_set_error(msg.c_str());
        return XBIM_ERROR;
    }
}

XBIM_EXPORT void XBIM_CALL xbim_buffer_free(unsigned char* buffer)
{
    std::free(buffer);
}

#pragma endregion
