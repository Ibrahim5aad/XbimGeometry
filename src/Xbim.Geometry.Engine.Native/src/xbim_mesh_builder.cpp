/*
 * xbim_mesh_builder.cpp
 *
 * Builds triangle meshes for common IFC solid types (extrusions, half-spaces)
 * and returns them as Manifold mesh handles, ready for boolean operations.
 * Eliminates the need to build meshes in managed code and round-trip them
 * through P/Invoke.
 */

#include "xbim_geometry_api.h"
#include "xbim_error.h"
#include "xbim_manifold_internal.h"

#include <manifold/cross_section.h>

#include <cmath>
#include <cstring>
#include <vector>

static void normalize3(double& x, double& y, double& z) {
    double len = std::sqrt(x * x + y * y + z * z);
    if (len > 1e-12) { x /= len; y /= len; z /= len; }
}

static double bboxDiagonal(const XbimBBox& bbox) {
    double sx = bbox.max.x - bbox.min.x;
    double sy = bbox.max.y - bbox.min.y;
    double sz = bbox.max.z - bbox.min.z;
    return std::sqrt(sx * sx + sy * sy + sz * sz);
}

// Extract axis columns from row-major 3x4 layout.
// Layout: [Xx Yx Zx Ox, Xy Yy Zy Oy, Xz Yz Zz Oz]
// X axis = (r[0], r[4], r[8]), Y = (r[1], r[5], r[9]), etc.
static void unpackMat3x4(const XbimMat3x4& m,
    double& Xx, double& Xy, double& Xz,
    double& Yx, double& Yy, double& Yz,
    double& Zx, double& Zy, double& Zz,
    double& Ox, double& Oy, double& Oz)
{
    Xx = m.r[0]; Yx = m.r[1]; Zx = m.r[2]; Ox = m.r[3];
    Xy = m.r[4]; Yy = m.r[5]; Zy = m.r[6]; Oy = m.r[7];
    Xz = m.r[8]; Yz = m.r[9]; Zz = m.r[10]; Oz = m.r[11];
}

// Build Polygons (vector of SimplePolygon) from profile contours.
static manifold::Polygons buildPolygons(const XbimProfileContours& profile)
{
    manifold::Polygons polygons;

    // Outer contour
    manifold::SimplePolygon outerPoly;
    outerPoly.reserve(profile.outer_count);
    for (int i = 0; i < profile.outer_count; i++)
        outerPoly.push_back({profile.outer_points[i * 2], profile.outer_points[i * 2 + 1]});
    polygons.push_back(std::move(outerPoly));

    // Inner contours (holes)
    const double* ptr = profile.inner_points;
    for (int h = 0; h < profile.num_inners; h++) {
        manifold::SimplePolygon innerPoly;
        innerPoly.reserve(profile.inner_sizes[h]);
        for (int i = 0; i < profile.inner_sizes[h]; i++)
            innerPoly.push_back({ptr[i * 2], ptr[i * 2 + 1]});
        polygons.push_back(std::move(innerPoly));
        ptr += profile.inner_sizes[h] * 2;
    }

    return polygons;
}

// Check if a polygon set is degenerate (all points collinear or too few).
static bool isPolygonDegenerate(const manifold::Polygons& polygons) {
    if (polygons.empty() || polygons[0].size() < 3) return true;
    // Quick area check on outer contour
    const auto& outer = polygons[0];
    double area = 0;
    int n = static_cast<int>(outer.size());
    for (int i = 0; i < n; i++) {
        int j = (i + 1) % n;
        area += outer[i].x * outer[j].y - outer[j].x * outer[i].y;
    }
    return std::abs(area) < 1e-12;
}

#pragma region Mesh Builder

XBIM_EXPORT XbimResult XBIM_CALL xbim_manifold_mesh_extrude(
    const XbimExtrusionParams* params,
    XbimManifoldMeshHandle* outHandle)
{
    if (!outHandle) return XBIM_INVALID_ARG;
    *outHandle = nullptr;

    if (!params) {
        xbim_set_error("xbim_manifold_mesh_extrude: null params");
        return XBIM_INVALID_ARG;
    }
    if (!params->profile.outer_points || params->profile.outer_count < 3) {
        xbim_set_error("xbim_manifold_mesh_extrude: need >= 3 outer vertices");
        return XBIM_INVALID_ARG;
    }
    if (params->depth <= 0) {
        xbim_set_error("xbim_manifold_mesh_extrude: depth must be positive");
        return XBIM_INVALID_ARG;
    }
    if (params->profile.num_inners > 0
        && (!params->profile.inner_points || !params->profile.inner_sizes)) {
        xbim_set_error("xbim_manifold_mesh_extrude: inner profiles require both data and sizes");
        return XBIM_INVALID_ARG;
    }

    try {
        auto polygons = buildPolygons(params->profile);

        if (isPolygonDegenerate(polygons)) {
            xbim_set_error("xbim_manifold_mesh_extrude: cross-section is degenerate");
            return XBIM_ERROR;
        }

        // Extrude along +Z by 1.0. The placement transform below maps
        // the unit-Z extrusion to the actual world-space direction and depth.
        auto extruded = manifold::Manifold::Extrude(polygons, 1.0);

        if (extruded.Status() != manifold::Manifold::Error::NoError) {
            xbim_set_error("xbim_manifold_mesh_extrude: Manifold::Extrude failed");
            return XBIM_ERROR;
        }

        // Extract placement axes (identity if no placement)
        double Xx = 1, Xy = 0, Xz = 0;
        double Yx = 0, Yy = 1, Yz = 0;
        double Zx = 0, Zy = 0, Zz = 1;
        double Ox = 0, Oy = 0, Oz = 0;

        if (params->placement)
            unpackMat3x4(*params->placement, Xx, Xy, Xz, Yx, Yy, Yz, Zx, Zy, Zz, Ox, Oy, Oz);

        // Transform extrusion direction from local to world and scale by depth.
        double edx = params->extrusion_dir.x;
        double edy = params->extrusion_dir.y;
        double edz = params->extrusion_dir.z;
        double col2x = (edx * Xx + edy * Yx + edz * Zx) * params->depth;
        double col2y = (edx * Xy + edy * Yy + edz * Zy) * params->depth;
        double col2z = (edx * Xz + edy * Yz + edz * Zz) * params->depth;

        // Final transform:
        //   col 0 = X axis (maps profile X to world)
        //   col 1 = Y axis (maps profile Y to world)
        //   col 2 = extrusion direction * depth (maps unit Z to world)
        //   col 3 = origin
        manifold::mat3x4 mat(
            {Xx, Xy, Xz},
            {Yx, Yy, Yz},
            {col2x, col2y, col2z},
            {Ox, Oy, Oz}
        );

        auto result = extruded.Transform(mat);

        if (result.Status() != manifold::Manifold::Error::NoError) {
            xbim_set_error("xbim_manifold_mesh_extrude: transform produced invalid manifold");
            return XBIM_ERROR;
        }

        auto* handle = new XbimManifoldMesh_();
        handle->manifold = std::move(result);
        *outHandle = handle;
        return XBIM_OK;
    }
    catch (const std::exception& ex) {
        xbim_set_error(ex.what());
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_manifold_mesh_halfspace(
    const XbimHalfSpaceParams* params,
    XbimManifoldMeshHandle* outHandle)
{
    if (!outHandle) return XBIM_INVALID_ARG;
    *outHandle = nullptr;

    if (!params) {
        xbim_set_error("xbim_manifold_mesh_halfspace: null params");
        return XBIM_INVALID_ARG;
    }

    try {
        double ox = params->plane_origin.x, oy = params->plane_origin.y, oz = params->plane_origin.z;
        double nx = params->plane_normal.x, ny = params->plane_normal.y, nz = params->plane_normal.z;
        double ax = params->plane_x_axis.x, ay = params->plane_x_axis.y, az = params->plane_x_axis.z;

        // Y = N x X
        double bx = ny * az - nz * ay;
        double by = nz * ax - nx * az;
        double bz = nx * ay - ny * ax;
        normalize3(bx, by, bz);

        double extent = std::max(bboxDiagonal(params->body_bbox) * 2.0, 1.0);

        // Material direction: agreement=1 means material on -N side
        double dx, dy, dz;
        if (params->agreement_flag) {
            dx = -nx; dy = -ny; dz = -nz;
        } else {
            dx = nx; dy = ny; dz = nz;
        }

        // 8-vertex box: 4 corners on the plane, 4 offset into material
        double eAx = extent * ax, eAy = extent * ay, eAz = extent * az;
        double eBx = extent * bx, eBy = extent * by, eBz = extent * bz;
        double offX = extent * dx, offY = extent * dy, offZ = extent * dz;

        float positions[8 * 3];
        auto setV = [&](int idx, double x, double y, double z) {
            positions[idx * 3]     = static_cast<float>(x);
            positions[idx * 3 + 1] = static_cast<float>(y);
            positions[idx * 3 + 2] = static_cast<float>(z);
        };

        setV(0, ox - eAx - eBx, oy - eAy - eBy, oz - eAz - eBz);
        setV(1, ox + eAx - eBx, oy + eAy - eBy, oz + eAz - eBz);
        setV(2, ox + eAx + eBx, oy + eAy + eBy, oz + eAz + eBz);
        setV(3, ox - eAx + eBx, oy - eAy + eBy, oz - eAz + eBz);
        setV(4, ox - eAx - eBx + offX, oy - eAy - eBy + offY, oz - eAz - eBz + offZ);
        setV(5, ox + eAx - eBx + offX, oy + eAy - eBy + offY, oz + eAz - eBz + offZ);
        setV(6, ox + eAx + eBx + offX, oy + eAy + eBy + offY, oz + eAz + eBz + offZ);
        setV(7, ox - eAx + eBx + offX, oy - eAy + eBy + offY, oz - eAz + eBz + offZ);

        static const unsigned int indices[] = {
            0,2,1, 0,3,2,   // bottom (-dir)
            4,5,6, 4,6,7,   // top (+dir)
            0,1,5, 0,5,4,   // front (-B)
            2,3,7, 2,7,6,   // back (+B)
            0,4,7, 0,7,3,   // left (-A)
            1,2,6, 1,6,5,   // right (+A)
        };

        manifold::MeshGL meshgl;
        meshgl.numProp = 3;
        meshgl.vertProperties.assign(positions, positions + 24);
        meshgl.triVerts.resize(36);
        for (int i = 0; i < 36; i++)
            meshgl.triVerts[i] = indices[i];

        auto* handle = new XbimManifoldMesh_();
        handle->manifold = manifold::Manifold(meshgl);

        if (handle->manifold.Status() != manifold::Manifold::Error::NoError) {
            xbim_set_error("xbim_manifold_mesh_halfspace: result is not valid manifold");
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

XBIM_EXPORT XbimResult XBIM_CALL xbim_manifold_mesh_halfspace_polygonal(
    const XbimPolyHalfSpaceParams* params,
    XbimManifoldMeshHandle* outHandle)
{
    if (!outHandle) return XBIM_INVALID_ARG;
    *outHandle = nullptr;

    if (!params) {
        xbim_set_error("xbim_manifold_mesh_halfspace_polygonal: null params");
        return XBIM_INVALID_ARG;
    }
    if (!params->polygon_points || params->polygon_count < 3) {
        xbim_set_error("xbim_manifold_mesh_halfspace_polygonal: need >= 3 polygon vertices");
        return XBIM_INVALID_ARG;
    }
    if (!params->boundary_placement) {
        xbim_set_error("xbim_manifold_mesh_halfspace_polygonal: null boundary placement");
        return XBIM_INVALID_ARG;
    }

    try {
        // Extract boundary placement axes
        double bXx, bXy, bXz, bYx, bYy, bYz, bZx, bZy, bZz, bOx, bOy, bOz;
        unpackMat3x4(*params->boundary_placement,
            bXx, bXy, bXz, bYx, bYy, bYz, bZx, bZy, bZz, bOx, bOy, bOz);

        double plNx = params->plane_normal.x;
        double plNy = params->plane_normal.y;
        double plNz = params->plane_normal.z;
        double plOx = params->plane_origin.x;
        double plOy = params->plane_origin.y;
        double plOz = params->plane_origin.z;
        int n = params->polygon_count;

        // Project boundary origin onto the half-space plane along boundary Z
        double nDotBz = plNx * bZx + plNy * bZy + plNz * bZz;
        double projOx = bOx, projOy = bOy, projOz = bOz;
        if (std::abs(nDotBz) > 1e-12) {
            double t = ((plOx - bOx) * plNx + (plOy - bOy) * plNy + (plOz - bOz) * plNz) / nDotBz;
            projOx += t * bZx;
            projOy += t * bZy;
            projOz += t * bZz;
        }

        // Build polygon for extrusion
        manifold::Polygons polygons;
        manifold::SimplePolygon capPoly;
        capPoly.reserve(n);
        for (int i = 0; i < n; i++)
            capPoly.push_back({params->polygon_points[i * 2], params->polygon_points[i * 2 + 1]});
        polygons.push_back(std::move(capPoly));

        if (isPolygonDegenerate(polygons)) {
            xbim_set_error("xbim_manifold_mesh_halfspace_polygonal: polygon is degenerate");
            return XBIM_ERROR;
        }

        // Extrude by 1.0 along Z, then transform
        auto extruded = manifold::Manifold::Extrude(polygons, 1.0);

        if (extruded.Status() != manifold::Manifold::Error::NoError) {
            xbim_set_error("xbim_manifold_mesh_halfspace_polygonal: extrusion failed");
            return XBIM_ERROR;
        }

        // Material direction
        double dirX, dirY, dirZ;
        if (params->agreement_flag) {
            dirX = -plNx; dirY = -plNy; dirZ = -plNz;
        } else {
            dirX = plNx; dirY = plNy; dirZ = plNz;
        }

        double extDepth = std::max(bboxDiagonal(params->body_bbox) * 2.0, 1.0);

        // Transform: place the unit extrusion into world space.
        // col 0 = boundary X axis
        // col 1 = boundary Y axis
        // col 2 = material direction * depth
        // col 3 = projected boundary origin on the half-space plane
        manifold::mat3x4 mat(
            {bXx, bXy, bXz},
            {bYx, bYy, bYz},
            {dirX * extDepth, dirY * extDepth, dirZ * extDepth},
            {projOx, projOy, projOz}
        );

        auto result = extruded.Transform(mat);

        if (result.Status() != manifold::Manifold::Error::NoError) {
            xbim_set_error("xbim_manifold_mesh_halfspace_polygonal: transform produced invalid manifold");
            return XBIM_ERROR;
        }

        auto* handle = new XbimManifoldMesh_();
        handle->manifold = std::move(result);
        *outHandle = handle;
        return XBIM_OK;
    }
    catch (const std::exception& ex) {
        xbim_set_error(ex.what());
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_manifold_mesh_extrude_with_openings(
    const XbimCoplanarSubtractParams* params,
    XbimManifoldMeshHandle* outHandle)
{
    if (!outHandle) return XBIM_INVALID_ARG;
    *outHandle = nullptr;

    if (!params) {
        xbim_set_error("xbim_manifold_mesh_extrude_with_openings: null params");
        return XBIM_INVALID_ARG;
    }
    if (!params->body_profile.outer_points || params->body_profile.outer_count < 3) {
        xbim_set_error("xbim_manifold_mesh_extrude_with_openings: need >= 3 body outer vertices");
        return XBIM_INVALID_ARG;
    }
    if (params->depth <= 0) {
        xbim_set_error("xbim_manifold_mesh_extrude_with_openings: depth must be positive");
        return XBIM_INVALID_ARG;
    }
    if (params->num_openings > 0 && !params->opening_profiles) {
        xbim_set_error("xbim_manifold_mesh_extrude_with_openings: null opening_profiles with num_openings > 0");
        return XBIM_INVALID_ARG;
    }

    try {
        // Build body cross-section from profile contours
        auto bodyPolygons = buildPolygons(params->body_profile);
        if (isPolygonDegenerate(bodyPolygons)) {
            xbim_set_error("xbim_manifold_mesh_extrude_with_openings: body profile is degenerate");
            return XBIM_ERROR;
        }

        manifold::CrossSection bodySection(bodyPolygons, manifold::CrossSection::FillRule::Positive);

        // Subtract each opening profile
        for (int i = 0; i < params->num_openings; i++) {
            auto openingPolygons = buildPolygons(params->opening_profiles[i]);
            if (isPolygonDegenerate(openingPolygons))
                continue; // skip degenerate openings

            manifold::CrossSection openingSection(openingPolygons, manifold::CrossSection::FillRule::Positive);
            bodySection -= openingSection;
        }

        if (bodySection.IsEmpty()) {
            xbim_set_error("xbim_manifold_mesh_extrude_with_openings: result is empty after subtraction");
            return XBIM_ERROR;
        }

        // Convert back to polygons and extrude along +Z by 1.0
        auto resultPolygons = bodySection.ToPolygons();
        auto extruded = manifold::Manifold::Extrude(resultPolygons, 1.0);

        if (extruded.Status() != manifold::Manifold::Error::NoError) {
            xbim_set_error("xbim_manifold_mesh_extrude_with_openings: Manifold::Extrude failed");
            return XBIM_ERROR;
        }

        // Build placement transform (same logic as xbim_manifold_mesh_extrude)
        double Xx = 1, Xy = 0, Xz = 0;
        double Yx = 0, Yy = 1, Yz = 0;
        double Zx = 0, Zy = 0, Zz = 1;
        double Ox = 0, Oy = 0, Oz = 0;

        if (params->placement)
            unpackMat3x4(*params->placement, Xx, Xy, Xz, Yx, Yy, Yz, Zx, Zy, Zz, Ox, Oy, Oz);

        double edx = params->extrusion_dir.x;
        double edy = params->extrusion_dir.y;
        double edz = params->extrusion_dir.z;
        double col2x = (edx * Xx + edy * Yx + edz * Zx) * params->depth;
        double col2y = (edx * Xy + edy * Yy + edz * Zy) * params->depth;
        double col2z = (edx * Xz + edy * Yz + edz * Zz) * params->depth;

        manifold::mat3x4 mat(
            {Xx, Xy, Xz},
            {Yx, Yy, Yz},
            {col2x, col2y, col2z},
            {Ox, Oy, Oz}
        );

        auto result = extruded.Transform(mat);

        if (result.Status() != manifold::Manifold::Error::NoError) {
            xbim_set_error("xbim_manifold_mesh_extrude_with_openings: transform produced invalid manifold");
            return XBIM_ERROR;
        }

        auto* handle = new XbimManifoldMesh_();
        handle->manifold = std::move(result);
        *outHandle = handle;
        return XBIM_OK;
    }
    catch (const std::exception& ex) {
        xbim_set_error(ex.what());
        return XBIM_ERROR;
    }
}

#pragma endregion
