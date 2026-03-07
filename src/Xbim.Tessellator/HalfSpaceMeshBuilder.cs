using System;
using System.Collections.Generic;
using Xbim.Common.Geometry;

namespace Xbim.Tessellator
{
    /// <summary>
    /// Generates triangle meshes representing the material side of IFC half-space solids.
    /// Used as clipping tools for Manifold mesh boolean operations.
    /// </summary>
    public static class HalfSpaceMeshBuilder
    {
        /// <summary>
        /// Builds a large box mesh on the material side of a plane.
        /// </summary>
        /// <param name="ox">Plane origin X.</param>
        /// <param name="oy">Plane origin Y.</param>
        /// <param name="oz">Plane origin Z.</param>
        /// <param name="nx">Plane normal X (normalized).</param>
        /// <param name="ny">Plane normal Y (normalized).</param>
        /// <param name="nz">Plane normal Z (normalized).</param>
        /// <param name="ax">Plane X-axis direction (normalized).</param>
        /// <param name="ay">Plane X-axis direction (normalized).</param>
        /// <param name="az">Plane X-axis direction (normalized).</param>
        /// <param name="agreementFlag">True = material on normal side; false = opposite.</param>
        /// <param name="bodyBounds">Bounding box of the body being clipped.</param>
        /// <returns>Mesh data, or null if inputs are degenerate.</returns>
        public static (float[] Positions, uint[] Indices, XbimRect3D Bounds)?
            BuildPlanarHalfSpace(
                double ox, double oy, double oz,
                double nx, double ny, double nz,
                double ax, double ay, double az,
                bool agreementFlag,
                XbimRect3D bodyBounds)
        {
            // Y = N × X
            double bx = ny * az - nz * ay;
            double by = nz * ax - nx * az;
            double bz = nx * ay - ny * ax;
            ExtrusionMeshBuilder.Normalize(ref bx, ref by, ref bz);

            double diagonal = Math.Sqrt(
                bodyBounds.SizeX * bodyBounds.SizeX +
                bodyBounds.SizeY * bodyBounds.SizeY +
                bodyBounds.SizeZ * bodyBounds.SizeZ);
            double extent = Math.Max(diagonal * 2, 1.0);

            // Extrusion direction: into the material side
            // OCCT convention: agreementFlag=true places material on the OPPOSITE side of the
            // surface normal. The half-space tool solid extends away from the normal when true.
            double dx, dy, dz;
            if (agreementFlag)
            {
                dx = -nx; dy = -ny; dz = -nz;
            }
            else
            {
                dx = nx; dy = ny; dz = nz;
            }

            // 4 corners on the plane face
            // c0 = O - extent*A - extent*B
            // c1 = O + extent*A - extent*B
            // c2 = O + extent*A + extent*B
            // c3 = O - extent*A + extent*B
            var positions = new float[8 * 3];
            double eA_x = extent * ax, eA_y = extent * ay, eA_z = extent * az;
            double eB_x = extent * bx, eB_y = extent * by, eB_z = extent * bz;
            double off_x = extent * dx, off_y = extent * dy, off_z = extent * dz;

            // Bottom face (on the plane)
            SetVertex(positions, 0, ox - eA_x - eB_x, oy - eA_y - eB_y, oz - eA_z - eB_z);
            SetVertex(positions, 1, ox + eA_x - eB_x, oy + eA_y - eB_y, oz + eA_z - eB_z);
            SetVertex(positions, 2, ox + eA_x + eB_x, oy + eA_y + eB_y, oz + eA_z + eB_z);
            SetVertex(positions, 3, ox - eA_x + eB_x, oy - eA_y + eB_y, oz - eA_z + eB_z);

            // Top face (offset along extrusion direction)
            SetVertex(positions, 4, ox - eA_x - eB_x + off_x, oy - eA_y - eB_y + off_y, oz - eA_z - eB_z + off_z);
            SetVertex(positions, 5, ox + eA_x - eB_x + off_x, oy + eA_y - eB_y + off_y, oz + eA_z - eB_z + off_z);
            SetVertex(positions, 6, ox + eA_x + eB_x + off_x, oy + eA_y + eB_y + off_y, oz + eA_z + eB_z + off_z);
            SetVertex(positions, 7, ox - eA_x + eB_x + off_x, oy - eA_y + eB_y + off_y, oz - eA_z + eB_z + off_z);

            // 12 triangles (2 per face), outward-facing winding
            var indices = new uint[]
            {
                // Bottom face (facing -dir): 0,2,1, 0,3,2
                0, 2, 1,  0, 3, 2,
                // Top face (facing +dir): 4,5,6, 4,6,7
                4, 5, 6,  4, 6, 7,
                // Front face (-B): 0,1,5, 0,5,4
                0, 1, 5,  0, 5, 4,
                // Back face (+B): 2,3,7, 2,7,6
                2, 3, 7,  2, 7, 6,
                // Left face (-A): 0,4,7, 0,7,3
                0, 4, 7,  0, 7, 3,
                // Right face (+A): 1,2,6, 1,6,5
                1, 2, 6,  1, 6, 5,
            };

            var bounds = ComputeBounds(positions);
            return (positions, indices, bounds);
        }

        /// <summary>
        /// Builds an extruded polygon prism mesh for a polygonal bounded half-space.
        /// </summary>
        /// <param name="planeOx">Plane surface origin X.</param>
        /// <param name="planeOy">Plane surface origin Y.</param>
        /// <param name="planeOz">Plane surface origin Z.</param>
        /// <param name="planeNx">Plane surface normal X.</param>
        /// <param name="planeNy">Plane surface normal Y.</param>
        /// <param name="planeNz">Plane surface normal Z.</param>
        /// <param name="agreementFlag">Material side agreement flag.</param>
        /// <param name="polygon2D">2D boundary polygon points (X,Y pairs).</param>
        /// <param name="boundaryOx">Boundary placement origin X.</param>
        /// <param name="boundaryOy">Boundary placement origin Y.</param>
        /// <param name="boundaryOz">Boundary placement origin Z.</param>
        /// <param name="boundaryXx">Boundary X-axis direction ratios.</param>
        /// <param name="boundaryXy">Boundary X-axis direction ratios.</param>
        /// <param name="boundaryXz">Boundary X-axis direction ratios.</param>
        /// <param name="boundaryZx">Boundary Z-axis (normal) direction ratios.</param>
        /// <param name="boundaryZy">Boundary Z-axis (normal) direction ratios.</param>
        /// <param name="boundaryZz">Boundary Z-axis (normal) direction ratios.</param>
        /// <param name="bodyBounds">Bounding box of the body being clipped.</param>
        /// <returns>Mesh data, or null if inputs are degenerate or unsupported.</returns>
        public static (float[] Positions, uint[] Indices, XbimRect3D Bounds)?
            BuildPolygonalBoundedHalfSpace(
                double planeOx, double planeOy, double planeOz,
                double planeNx, double planeNy, double planeNz,
                bool agreementFlag,
                (double X, double Y)[] polygon2D,
                double boundaryOx, double boundaryOy, double boundaryOz,
                double boundaryXx, double boundaryXy, double boundaryXz,
                double boundaryZx, double boundaryZy, double boundaryZz,
                XbimRect3D bodyBounds)
        {
            if (polygon2D == null || polygon2D.Length < 3)
                return null;

            int n = polygon2D.Length;

            // Compute boundary Y axis = Z × X
            double boundaryYx = boundaryZy * boundaryXz - boundaryZz * boundaryXy;
            double boundaryYy = boundaryZz * boundaryXx - boundaryZx * boundaryXz;
            double boundaryYz = boundaryZx * boundaryXy - boundaryZy * boundaryXx;
            ExtrusionMeshBuilder.Normalize(ref boundaryYx, ref boundaryYy, ref boundaryYz);

            // Transform 2D polygon to 3D world coordinates using boundary placement
            var worldPts = new (double X, double Y, double Z)[n];
            for (int i = 0; i < n; i++)
            {
                double px = polygon2D[i].X;
                double py = polygon2D[i].Y;
                worldPts[i] = (
                    boundaryOx + px * boundaryXx + py * boundaryYx,
                    boundaryOy + px * boundaryXy + py * boundaryYy,
                    boundaryOz + px * boundaryXz + py * boundaryYz
                );
            }

            // Project polygon vertices onto the half-space plane along boundary Z.
            // The polygon sits in the XY plane of the boundary placement, which may be
            // offset from the half-space plane (e.g. boundary at Z=0, plane at Z=740).
            double nDotBz = planeNx * boundaryZx + planeNy * boundaryZy + planeNz * boundaryZz;
            if (Math.Abs(nDotBz) > 1e-12)
            {
                for (int i = 0; i < n; i++)
                {
                    // t = dot(planeOrigin - vertex, planeNormal) / dot(boundaryZ, planeNormal)
                    double diffX = planeOx - worldPts[i].X;
                    double diffY = planeOy - worldPts[i].Y;
                    double diffZ = planeOz - worldPts[i].Z;
                    double t = (diffX * planeNx + diffY * planeNy + diffZ * planeNz) / nDotBz;
                    worldPts[i] = (
                        worldPts[i].X + t * boundaryZx,
                        worldPts[i].Y + t * boundaryZy,
                        worldPts[i].Z + t * boundaryZz
                    );
                }
            }

            // Compute extrusion depth and direction
            double diagonal = Math.Sqrt(
                bodyBounds.SizeX * bodyBounds.SizeX +
                bodyBounds.SizeY * bodyBounds.SizeY +
                bodyBounds.SizeZ * bodyBounds.SizeZ);
            double depth = Math.Max(diagonal * 2, 1.0);

            // Material direction: agreement=TRUE → material on -N side, FALSE → +N side
            double dx, dy, dz;
            if (agreementFlag)
            {
                dx = -planeNx * depth;
                dy = -planeNy * depth;
                dz = -planeNz * depth;
            }
            else
            {
                dx = planeNx * depth;
                dy = planeNy * depth;
                dz = planeNz * depth;
            }

            // Build vertices: bottom ring (on plane) + top ring (offset into material)
            var positions = new float[(n * 2) * 3];
            for (int i = 0; i < n; i++)
            {
                // Bottom vertex (projected onto plane)
                SetVertex(positions, i, worldPts[i].X, worldPts[i].Y, worldPts[i].Z);
                // Top vertex (extruded into material side)
                SetVertex(positions, n + i, worldPts[i].X + dx, worldPts[i].Y + dy, worldPts[i].Z + dz);
            }

            // Tessellate caps using Tess (LibTessDotNet)
            var bottomCapIndices = TessellateCap(worldPts, flipWinding: true);
            var topCapIndices = TessellateCapOffset(worldPts, dx, dy, dz, n, flipWinding: false);

            // Side faces: 2 triangles per polygon edge
            var sideIndices = new List<uint>(n * 6);
            for (int i = 0; i < n; i++)
            {
                uint b0 = (uint)i;
                uint b1 = (uint)((i + 1) % n);
                uint t0 = (uint)(n + i);
                uint t1 = (uint)(n + (i + 1) % n);

                sideIndices.Add(b0); sideIndices.Add(b1); sideIndices.Add(t1);
                sideIndices.Add(b0); sideIndices.Add(t1); sideIndices.Add(t0);
            }

            // Combine all indices
            var allIndices = new uint[bottomCapIndices.Length + topCapIndices.Length + sideIndices.Count];
            bottomCapIndices.CopyTo(allIndices, 0);
            topCapIndices.CopyTo(allIndices, bottomCapIndices.Length);
            sideIndices.CopyTo(allIndices, bottomCapIndices.Length + topCapIndices.Length);

            var bounds = ComputeBounds(positions);
            return (positions, allIndices, bounds);
        }

        private static uint[] TessellateCap((double X, double Y, double Z)[] pts, bool flipWinding)
        {
            var tess = new Tess();
            var contour = new ContourVertex[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                contour[i] = new ContourVertex
                {
                    Position = new Vec3(pts[i].X, pts[i].Y, pts[i].Z),
                    Data = i
                };
            }
            tess.AddContour(contour);
            tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3);

            var elements = tess.Elements;
            var verts = tess.Vertices;
            var indices = new uint[tess.ElementCount * 3];

            for (int j = 0; j < tess.ElementCount; j++)
            {
                int i0 = verts[elements[j * 3]].Data;
                int i1 = verts[elements[j * 3 + 1]].Data;
                int i2 = verts[elements[j * 3 + 2]].Data;

                if (i0 < 0 || i1 < 0 || i2 < 0)
                    return Array.Empty<uint>(); // Tess inserted new vertices — bail to simple

                if (flipWinding)
                {
                    indices[j * 3] = (uint)i0;
                    indices[j * 3 + 1] = (uint)i2;
                    indices[j * 3 + 2] = (uint)i1;
                }
                else
                {
                    indices[j * 3] = (uint)i0;
                    indices[j * 3 + 1] = (uint)i1;
                    indices[j * 3 + 2] = (uint)i2;
                }
            }
            return indices;
        }

        private static uint[] TessellateCapOffset(
            (double X, double Y, double Z)[] pts,
            double dx, double dy, double dz,
            int vertexOffset, bool flipWinding)
        {
            var tess = new Tess();
            var contour = new ContourVertex[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                contour[i] = new ContourVertex
                {
                    Position = new Vec3(pts[i].X + dx, pts[i].Y + dy, pts[i].Z + dz),
                    Data = i
                };
            }
            tess.AddContour(contour);
            tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3);

            var elements = tess.Elements;
            var verts = tess.Vertices;
            var indices = new uint[tess.ElementCount * 3];

            for (int j = 0; j < tess.ElementCount; j++)
            {
                int i0 = verts[elements[j * 3]].Data;
                int i1 = verts[elements[j * 3 + 1]].Data;
                int i2 = verts[elements[j * 3 + 2]].Data;

                if (i0 < 0 || i1 < 0 || i2 < 0)
                    return Array.Empty<uint>();

                if (flipWinding)
                {
                    indices[j * 3] = (uint)(i0 + vertexOffset);
                    indices[j * 3 + 1] = (uint)(i2 + vertexOffset);
                    indices[j * 3 + 2] = (uint)(i1 + vertexOffset);
                }
                else
                {
                    indices[j * 3] = (uint)(i0 + vertexOffset);
                    indices[j * 3 + 1] = (uint)(i1 + vertexOffset);
                    indices[j * 3 + 2] = (uint)(i2 + vertexOffset);
                }
            }
            return indices;
        }

        private static void SetVertex(float[] positions, int index, double x, double y, double z)
        {
            positions[index * 3] = (float)x;
            positions[index * 3 + 1] = (float)y;
            positions[index * 3 + 2] = (float)z;
        }

        private static XbimRect3D ComputeBounds(float[] positions)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            for (int i = 0; i < positions.Length; i += 3)
            {
                double x = positions[i], y = positions[i + 1], z = positions[i + 2];
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (z < minZ) minZ = z;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
                if (z > maxZ) maxZ = z;
            }

            return new XbimRect3D(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ);
        }
    }
}
