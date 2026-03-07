/*
 * xbim_manifold.cpp
 *
 * Thin wrapper around the Manifold library for performing boolean operations
 * on triangle meshes. Provides mesh creation from raw vertex/index data,
 * boolean cut/union, affine transforms, and mesh data extraction.
 *
 */

#include "xbim_geometry_api.h"
#include "xbim_error.h"
#include "xbim_manifold_internal.h"

#include <cstdlib>
#include <cstring>
#include <sstream>
#include <vector>

#pragma region Manifold Mesh Booleans

XBIM_EXPORT XbimResult XBIM_CALL xbim_manifold_mesh_create(
    const float* positions, int numVerts,
    const unsigned int* indices, int numTris,
    XbimManifoldMeshHandle* outHandle)
{
    if (!outHandle) return XBIM_INVALID_ARG;
    *outHandle = nullptr;

    if (!positions || numVerts < 3 || !indices || numTris < 1) {
        xbim_set_error("xbim_manifold_mesh_create: invalid input (need >= 3 verts, >= 1 tri)");
        return XBIM_INVALID_ARG;
    }

    try {
        manifold::MeshGL meshgl;
        meshgl.numProp = 3;

        // Copy vertex positions
        meshgl.vertProperties.resize(numVerts * 3);
        std::memcpy(meshgl.vertProperties.data(), positions, numVerts * 3 * sizeof(float));

        // Copy triangle indices
        meshgl.triVerts.resize(numTris * 3);
        for (int i = 0; i < numTris * 3; i++) {
            meshgl.triVerts[i] = static_cast<uint32_t>(indices[i]);
        }

        auto* handle = new XbimManifoldMesh_();
        handle->manifold = manifold::Manifold(meshgl);

        auto status = handle->manifold.Status();
        if (status != manifold::Manifold::Error::NoError) {
            std::ostringstream oss;
            oss << "xbim_manifold_mesh_create: mesh is not valid manifold (error code "
                << static_cast<int>(status) << ")";
            xbim_set_error(oss.str().c_str());
            delete handle;
            return XBIM_ERROR;
        }

        *outHandle = handle;
        return XBIM_OK;
    }
    catch (const std::exception& ex) {
        xbim_set_error(ex.what());
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_manifold_boolean_cut(
    XbimManifoldMeshHandle body, XbimManifoldMeshHandle tool,
    XbimManifoldMeshHandle* outHandle)
{
    if (!outHandle) return XBIM_INVALID_ARG;
    *outHandle = nullptr;

    if (!body || !tool) {
        xbim_set_error("xbim_manifold_boolean_cut: null handle");
        return XBIM_INVALID_HANDLE;
    }

    try {
        auto* result = new XbimManifoldMesh_();
        result->manifold = body->manifold - tool->manifold;

        if (result->manifold.Status() != manifold::Manifold::Error::NoError) {
            xbim_set_error("xbim_manifold_boolean_cut: boolean difference failed");
            delete result;
            return XBIM_ERROR;
        }

        *outHandle = result;
        return XBIM_OK;
    }
    catch (const std::exception& ex) {
        xbim_set_error(ex.what());
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_manifold_boolean_union(
    XbimManifoldMeshHandle body, XbimManifoldMeshHandle tool,
    XbimManifoldMeshHandle* outHandle)
{
    if (!outHandle) return XBIM_INVALID_ARG;
    *outHandle = nullptr;

    if (!body || !tool) {
        xbim_set_error("xbim_manifold_boolean_union: null handle");
        return XBIM_INVALID_HANDLE;
    }

    try {
        auto* result = new XbimManifoldMesh_();
        result->manifold = body->manifold + tool->manifold;

        if (result->manifold.Status() != manifold::Manifold::Error::NoError) {
            xbim_set_error("xbim_manifold_boolean_union: boolean union failed");
            delete result;
            return XBIM_ERROR;
        }

        *outHandle = result;
        return XBIM_OK;
    }
    catch (const std::exception& ex) {
        xbim_set_error(ex.what());
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_manifold_boolean_cut_multi(
    XbimManifoldMeshHandle body,
    XbimManifoldMeshHandle* tools, int numTools,
    XbimManifoldMeshHandle* outHandle)
{
    if (!outHandle) return XBIM_INVALID_ARG;
    *outHandle = nullptr;

    if (!body) {
        xbim_set_error("xbim_manifold_boolean_cut_multi: null body handle");
        return XBIM_INVALID_HANDLE;
    }
    if (!tools || numTools < 1) {
        xbim_set_error("xbim_manifold_boolean_cut_multi: no tools provided");
        return XBIM_INVALID_ARG;
    }

    try {
        // Union all tools first via BatchBoolean, then single difference
        std::vector<manifold::Manifold> toolManifolds;
        toolManifolds.reserve(numTools);
        for (int i = 0; i < numTools; i++) {
            if (!tools[i]) {
                xbim_set_error("xbim_manifold_boolean_cut_multi: null tool handle");
                return XBIM_INVALID_HANDLE;
            }
            toolManifolds.push_back(tools[i]->manifold);
        }

        manifold::Manifold toolUnion;
        if (numTools == 1) {
            toolUnion = std::move(toolManifolds[0]);
        } else {
            toolUnion = manifold::Manifold::BatchBoolean(toolManifolds,
                                                          manifold::OpType::Add);
        }

        auto* result = new XbimManifoldMesh_();
        result->manifold = body->manifold - toolUnion;

        if (result->manifold.Status() != manifold::Manifold::Error::NoError) {
            xbim_set_error("xbim_manifold_boolean_cut_multi: batch boolean difference failed");
            delete result;
            return XBIM_ERROR;
        }

        *outHandle = result;
        return XBIM_OK;
    }
    catch (const std::exception& ex) {
        xbim_set_error(ex.what());
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_manifold_mesh_transform(
    XbimManifoldMeshHandle mesh, const double* matrix3x4,
    XbimManifoldMeshHandle* outHandle)
{
    if (!outHandle) return XBIM_INVALID_ARG;
    *outHandle = nullptr;

    if (!mesh) {
        xbim_set_error("xbim_manifold_mesh_transform: null handle");
        return XBIM_INVALID_HANDLE;
    }
    if (!matrix3x4) {
        xbim_set_error("xbim_manifold_mesh_transform: null matrix");
        return XBIM_INVALID_ARG;
    }

    try {
        // Manifold::Transform takes a mat3x4 (4 columns of vec3, column-major).
        // Our input is row-major: [r00 r01 r02 tx, r10 r11 r12 ty, r20 r21 r22 tz].
        // Transpose rows to columns for the mat3x4 constructor.
        manifold::mat3x4 m(
            {matrix3x4[0], matrix3x4[4], matrix3x4[8]},   // col 0: r00, r10, r20
            {matrix3x4[1], matrix3x4[5], matrix3x4[9]},   // col 1: r01, r11, r21
            {matrix3x4[2], matrix3x4[6], matrix3x4[10]},  // col 2: r02, r12, r22
            {matrix3x4[3], matrix3x4[7], matrix3x4[11]}   // col 3: tx,  ty,  tz
        );

        auto* result = new XbimManifoldMesh_();
        result->manifold = mesh->manifold.Transform(m);

        *outHandle = result;
        return XBIM_OK;
    }
    catch (const std::exception& ex) {
        xbim_set_error(ex.what());
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_manifold_mesh_get_data(
    XbimManifoldMeshHandle mesh,
    int* outNumVerts, int* outNumTris,
    float** outPositions, unsigned int** outIndices)
{
    if (!mesh) {
        xbim_set_error("xbim_manifold_mesh_get_data: null handle");
        return XBIM_INVALID_HANDLE;
    }
    if (!outNumVerts || !outNumTris || !outPositions || !outIndices) {
        xbim_set_error("xbim_manifold_mesh_get_data: null output parameter");
        return XBIM_INVALID_ARG;
    }

    try {
        auto meshgl = mesh->manifold.GetMeshGL();

        int numVerts = static_cast<int>(meshgl.NumVert());
        int numTris = static_cast<int>(meshgl.NumTri());

        // Allocate output buffers
        float* positions = static_cast<float*>(std::malloc(numVerts * 3 * sizeof(float)));
        unsigned int* indices = static_cast<unsigned int*>(std::malloc(numTris * 3 * sizeof(unsigned int)));

        if (!positions || !indices) {
            std::free(positions);
            std::free(indices);
            xbim_set_error("xbim_manifold_mesh_get_data: allocation failed");
            return XBIM_ERROR;
        }

        // Copy vertex positions (MeshGL stores interleaved properties, numProp=3 for xyz)
        int numProp = static_cast<int>(meshgl.numProp);
        for (int i = 0; i < numVerts; i++) {
            positions[i * 3 + 0] = meshgl.vertProperties[i * numProp + 0];
            positions[i * 3 + 1] = meshgl.vertProperties[i * numProp + 1];
            positions[i * 3 + 2] = meshgl.vertProperties[i * numProp + 2];
        }

        // Copy triangle indices
        for (int i = 0; i < numTris * 3; i++) {
            indices[i] = static_cast<unsigned int>(meshgl.triVerts[i]);
        }

        *outNumVerts = numVerts;
        *outNumTris = numTris;
        *outPositions = positions;
        *outIndices = indices;
        return XBIM_OK;
    }
    catch (const std::exception& ex) {
        xbim_set_error(ex.what());
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_manifold_mesh_bounding_box(
    XbimManifoldMeshHandle mesh,
    double* outMinX, double* outMinY, double* outMinZ,
    double* outMaxX, double* outMaxY, double* outMaxZ)
{
    if (!mesh) {
        xbim_set_error("xbim_manifold_mesh_bounding_box: null handle");
        return XBIM_INVALID_HANDLE;
    }
    if (!outMinX || !outMinY || !outMinZ || !outMaxX || !outMaxY || !outMaxZ) {
        xbim_set_error("xbim_manifold_mesh_bounding_box: null output parameter");
        return XBIM_INVALID_ARG;
    }

    try {
        auto box = mesh->manifold.BoundingBox();
        *outMinX = box.min.x;
        *outMinY = box.min.y;
        *outMinZ = box.min.z;
        *outMaxX = box.max.x;
        *outMaxY = box.max.y;
        *outMaxZ = box.max.z;
        return XBIM_OK;
    }
    catch (const std::exception& ex) {
        xbim_set_error(ex.what());
        return XBIM_ERROR;
    }
}

XBIM_EXPORT int XBIM_CALL xbim_manifold_mesh_is_valid(
    XbimManifoldMeshHandle mesh)
{
    if (!mesh) return XBIM_FALSE;
    return mesh->manifold.Status() == manifold::Manifold::Error::NoError
        ? XBIM_TRUE : XBIM_FALSE;
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_manifold_mesh_destroy(
    XbimManifoldMeshHandle handle)
{
    if (!handle) return XBIM_OK; // safe no-op
    delete handle;
    return XBIM_OK;
}

XBIM_EXPORT void XBIM_CALL xbim_buffer_free_float(float* buffer)
{
    std::free(buffer);
}

XBIM_EXPORT void XBIM_CALL xbim_buffer_free_uint(unsigned int* buffer)
{
    std::free(buffer);
}

#pragma endregion
