using System;
using System.Collections.Generic;
using Xbim.Common.Geometry;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Tessellator
{
    public enum CapMode { Both, None, StartCapOnly, EndCapOnly }

    public static class ExtrusionMeshBuilder
    {
        public static XbimTriangulatedMesh Build(
            ProfilePolygonBuilder.ProfilePolygons polygons,
            (double X, double Y, double Z) extrusionDir,
            double depth,
            IIfcAxis2Placement3D solidPosition,
            float precision,
            Func<XbimTriangulatedMesh, int, XbimTriangulatedMesh> postCallback,
            int entityLabel,
            CapMode capMode = CapMode.Both)
        {
            // Extract solid position axes (identity if null)
            GetSolidAxes(solidPosition,
                out double ox, out double oy, out double oz,
                out double xx, out double xy, out double xz,
                out double yx, out double yy, out double yz,
                out double zx, out double zy, out double zz);

            // Transform extrusion direction to world coordinates
            double dxw = extrusionDir.X * xx + extrusionDir.Y * yx + extrusionDir.Z * zx;
            double dyw = extrusionDir.X * xy + extrusionDir.Y * yy + extrusionDir.Z * zy;
            double dzw = extrusionDir.X * xz + extrusionDir.Y * yz + extrusionDir.Z * zz;

            // Scale direction by depth
            double offX = dxw * depth;
            double offY = dyw * depth;
            double offZ = dzw * depth;

            // Count edges for face estimation
            int outerCount = polygons.Outer.Length;
            int totalEdges = outerCount;
            int innerVertexCount = 0;
            if (polygons.Inners != null)
            {
                foreach (var inner in polygons.Inners)
                {
                    totalEdges += inner.Length;
                    innerVertexCount += inner.Length;
                }
            }

            int capCount = capMode switch
            {
                CapMode.Both => 2,
                CapMode.None => 0,
                _ => 1
            };
            int estimatedFaces = capCount + totalEdges;

            var mesh = new XbimTriangulatedMesh(estimatedFaces, precision);

            // === Add bottom (start) and top (end) vertices ===
            // We track positions separately for cap tessellation since XbimTriangulatedMesh
            // doesn't expose random-access vertex positions.
            int totalVerts = outerCount + innerVertexCount;
            var bottomPositions = new Vec3[totalVerts];
            var bottomIndices = new int[totalVerts];
            var topIndices = new int[totalVerts];

            int vi = 0;

            // Outer loop
            for (int i = 0; i < outerCount; i++)
            {
                double px = polygons.Outer[i].X;
                double py = polygons.Outer[i].Y;

                // Transform 2D profile point to 3D world: O + px*X + py*Y
                double wx = ox + px * xx + py * yx;
                double wy = oy + px * xy + py * yy;
                double wz = oz + px * xz + py * yz;

                var bottomV = new Vec3(wx, wy, wz);
                var topV = new Vec3(wx + offX, wy + offY, wz + offZ);

                bottomPositions[vi] = bottomV;
                bottomIndices[vi] = mesh.AddVertex(bottomV);
                topIndices[vi] = mesh.AddVertex(topV);
                vi++;
            }

            // Inner loops
            var innerLoopStarts = new List<int>();
            var innerLoopLengths = new List<int>();
            if (polygons.Inners != null)
            {
                foreach (var inner in polygons.Inners)
                {
                    innerLoopStarts.Add(vi);
                    innerLoopLengths.Add(inner.Length);
                    for (int i = 0; i < inner.Length; i++)
                    {
                        double px = inner[i].X;
                        double py = inner[i].Y;

                        double wx = ox + px * xx + py * yx;
                        double wy = oy + px * xy + py * yy;
                        double wz = oz + px * xz + py * yz;

                        var bottomV = new Vec3(wx, wy, wz);
                        var topV = new Vec3(wx + offX, wy + offY, wz + offZ);

                        bottomPositions[vi] = bottomV;
                        bottomIndices[vi] = mesh.AddVertex(bottomV);
                        topIndices[vi] = mesh.AddVertex(topV);
                        vi++;
                    }
                }
            }

            int faceId = 0;

            // === Start cap (bottom face) ===
            if (capMode == CapMode.Both || capMode == CapMode.StartCapOnly)
            {
                TessellateCap(mesh, bottomPositions, bottomIndices,
                    outerCount, innerLoopStarts, innerLoopLengths,
                    ref faceId, flipWinding: true);
            }

            // === End cap (top face) ===
            if (capMode == CapMode.Both || capMode == CapMode.EndCapOnly)
            {
                // Build top positions for cap tessellation
                var topPositions = new Vec3[totalVerts];
                for (int i = 0; i < totalVerts; i++)
                    topPositions[i] = new Vec3(
                        bottomPositions[i].X + offX,
                        bottomPositions[i].Y + offY,
                        bottomPositions[i].Z + offZ);

                TessellateCap(mesh, topPositions, topIndices,
                    outerCount, innerLoopStarts, innerLoopLengths,
                    ref faceId, flipWinding: false);
            }

            // === Side faces ===
            // Outer loop sides
            AddSideFaces(mesh, bottomIndices, topIndices, 0, outerCount, ref faceId);

            // Inner loop sides
            for (int h = 0; h < innerLoopStarts.Count; h++)
            {
                int start = innerLoopStarts[h];
                int length = innerLoopLengths[h];
                AddSideFaces(mesh, bottomIndices, topIndices, start, length, ref faceId);
            }

            // Post-tessellation callback
            if (postCallback != null)
                mesh = postCallback(mesh, entityLabel);

            mesh.UnifyFaceOrientation();
            return mesh;
        }

        private static void AddSideFaces(XbimTriangulatedMesh mesh,
            int[] bottomIndices, int[] topIndices,
            int loopStart, int loopLength, ref int faceId)
        {
            for (int i = 0; i < loopLength; i++)
            {
                int curr = loopStart + i;
                int next = loopStart + (i + 1) % loopLength;

                int b0 = bottomIndices[curr];
                int b1 = bottomIndices[next];
                int t0 = topIndices[curr];
                int t1 = topIndices[next];

                mesh.AddTriangle(b0, b1, t1, faceId);
                mesh.AddTriangle(b0, t1, t0, faceId);
                faceId++;
            }
        }

        private static void TessellateCap(XbimTriangulatedMesh mesh,
            Vec3[] positions, int[] indices,
            int outerCount, List<int> innerStarts, List<int> innerLengths,
            ref int faceId, bool flipWinding)
        {
            var contours = new List<ContourVertex[]>();

            // Outer contour
            var outerContour = new ContourVertex[outerCount];
            for (int i = 0; i < outerCount; i++)
            {
                outerContour[i] = new ContourVertex
                {
                    Position = positions[i],
                    Data = indices[i]
                };
            }
            contours.Add(outerContour);

            // Inner contours
            for (int h = 0; h < innerStarts.Count; h++)
            {
                int start = innerStarts[h];
                int length = innerLengths[h];
                var innerContour = new ContourVertex[length];
                for (int i = 0; i < length; i++)
                {
                    innerContour[i] = new ContourVertex
                    {
                        Position = positions[start + i],
                        Data = indices[start + i]
                    };
                }
                contours.Add(innerContour);
            }

            var tess = new Tess();
            tess.AddContours(contours);
            tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3);

            var elements = tess.Elements;
            var verts = tess.Vertices;

            // Resolve any newly-inserted vertices
            for (int j = 0; j < tess.ElementCount * 3; j++)
            {
                if (verts[elements[j]].Data < 0)
                    mesh.AddVertex(verts[elements[j]].Position, ref verts[elements[j]]);
            }

            for (int j = 0; j < tess.ElementCount; j++)
            {
                int i0 = verts[elements[j * 3]].Data;
                int i1 = verts[elements[j * 3 + 1]].Data;
                int i2 = verts[elements[j * 3 + 2]].Data;

                if (flipWinding)
                    mesh.AddTriangle(i0, i2, i1, faceId);
                else
                    mesh.AddTriangle(i0, i1, i2, faceId);
            }
            faceId++;
        }

        public static void GetSolidAxes(IIfcAxis2Placement3D position,
            out double ox, out double oy, out double oz,
            out double xx, out double xy, out double xz,
            out double yx, out double yy, out double yz,
            out double zx, out double zy, out double zz)
        {
            if (position == null)
            {
                // Identity
                ox = oy = oz = 0;
                xx = 1; xy = 0; xz = 0;
                yx = 0; yy = 1; yz = 0;
                zx = 0; zy = 0; zz = 1;
                return;
            }

            ox = position.Location.Coordinates[0];
            oy = position.Location.Coordinates[1];
            oz = position.Location.Dim == 3 ? position.Location.Coordinates[2] : 0;

            // Z axis (default 0,0,1)
            if (position.Axis != null)
            {
                zx = position.Axis.DirectionRatios[0];
                zy = position.Axis.DirectionRatios[1];
                zz = position.Axis.DirectionRatios.Count > 2 ? position.Axis.DirectionRatios[2] : 0;
                Normalize(ref zx, ref zy, ref zz);
            }
            else
            {
                zx = 0; zy = 0; zz = 1;
            }

            // X axis (default 1,0,0 — or computed to be perpendicular to Z)
            if (position.RefDirection != null)
            {
                xx = position.RefDirection.DirectionRatios[0];
                xy = position.RefDirection.DirectionRatios[1];
                xz = position.RefDirection.DirectionRatios.Count > 2 ? position.RefDirection.DirectionRatios[2] : 0;
                // Orthogonalize X against Z: X = X - (X·Z)Z
                double dot = xx * zx + xy * zy + xz * zz;
                xx -= dot * zx;
                xy -= dot * zy;
                xz -= dot * zz;
                Normalize(ref xx, ref xy, ref xz);
            }
            else
            {
                // Default X perpendicular to Z
                DefaultPerpendicular(zx, zy, zz, out xx, out xy, out xz);
            }

            // Y = Z × X
            yx = zy * xz - zz * xy;
            yy = zz * xx - zx * xz;
            yz = zx * xy - zy * xx;
        }

        public static void Normalize(ref double x, ref double y, ref double z)
        {
            double len = Math.Sqrt(x * x + y * y + z * z);
            if (len > 1e-12) { x /= len; y /= len; z /= len; }
        }

        public static void DefaultPerpendicular(double zx, double zy, double zz,
            out double xx, out double xy, out double xz)
        {
            // Pick axis most perpendicular to Z, then cross
            if (Math.Abs(zx) < Math.Abs(zy) && Math.Abs(zx) < Math.Abs(zz))
            {
                // Z is mostly in Y-Z plane, cross with X axis
                xx = 0; xy = -zz; xz = zy;
            }
            else if (Math.Abs(zy) < Math.Abs(zz))
            {
                // Z is mostly in X-Z plane, cross with Y axis
                xx = zz; xy = 0; xz = -zx;
            }
            else
            {
                // Z is mostly in X-Y plane, cross with Z axis
                xx = -zy; xy = zx; xz = 0;
            }
            Normalize(ref xx, ref xy, ref xz);
        }
    }
}
