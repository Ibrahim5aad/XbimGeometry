using System.Runtime.InteropServices;
using Xbim.Common.Geometry;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;

namespace Xbim.Geometry.Engine.Services
{
    /// <summary>
    /// Performs boolean operations on triangle meshes using the Manifold library.
    /// Operates on raw vertex/index data — no OCCT BRep shapes involved.
    /// </summary>
    internal class ManifoldMeshBooleanService
    {
        /// <summary>
        /// Raw triangle mesh data with flat vertex positions and triangle indices.
        /// </summary>
        public readonly struct MeshData
        {
            public readonly float[] Positions;  // [x0,y0,z0, x1,y1,z1, ...]
            public readonly uint[] Indices;      // [i0,i1,i2, ...]
            public readonly XbimRect3D Bounds;

            public MeshData(float[] positions, uint[] indices, XbimRect3D bounds)
            {
                Positions = positions;
                Indices = indices;
                Bounds = bounds;
            }

            public int VertexCount => Positions.Length / 3;
            public int TriangleCount => Indices.Length / 3;
        }

        /// <summary>
        /// Subtract opening meshes from the body mesh.
        /// Returns null on failure (caller should fall back to OCCT).
        /// </summary>
        public MeshData? Cut(MeshData body, IReadOnlyList<MeshData> tools)
        {
            if (tools == null || tools.Count == 0) return body;

            NativeManifoldMeshHandle bodyHandle = null;
            var toolHandles = new NativeManifoldMeshHandle[tools.Count];

            try
            {
                bodyHandle = CreateManifoldMesh(body);
                if (bodyHandle == null || bodyHandle.IsInvalid) return null;

                for (int i = 0; i < tools.Count; i++)
                {
                    toolHandles[i] = CreateManifoldMesh(tools[i]);
                    if (toolHandles[i] == null || toolHandles[i].IsInvalid) return null;
                }

                NativeManifoldMeshHandle resultHandle;

                if (tools.Count == 1)
                {
                    int rc = XbimGeometryNativeApi.xbim_manifold_boolean_cut(
                        bodyHandle, toolHandles[0], out resultHandle);
                    if (rc != 0) return null;
                }
                else
                {
                    var toolPtrs = new IntPtr[tools.Count];
                    for (int i = 0; i < tools.Count; i++)
                        toolPtrs[i] = toolHandles[i].DangerousGetHandle();

                    int rc = XbimGeometryNativeApi.xbim_manifold_boolean_cut_multi(
                        bodyHandle, toolPtrs, tools.Count, out resultHandle);
                    if (rc != 0) return null;
                }

                using (resultHandle)
                {
                    return ExtractMeshData(resultHandle);
                }
            }
            finally
            {
                bodyHandle?.Dispose();
                foreach (var h in toolHandles)
                    h?.Dispose();
            }
        }

        /// <summary>
        /// Union two meshes together.
        /// Returns null on failure.
        /// </summary>
        public MeshData? Union(MeshData a, MeshData b)
        {
            using var handleA = CreateManifoldMesh(a);
            using var handleB = CreateManifoldMesh(b);

            if (handleA == null || handleA.IsInvalid ||
                handleB == null || handleB.IsInvalid)
                return null;

            int rc = XbimGeometryNativeApi.xbim_manifold_boolean_union(
                handleA, handleB, out var resultHandle);
            if (rc != 0) return null;

            using (resultHandle)
            {
                return ExtractMeshData(resultHandle);
            }
        }

        /// <summary>
        /// Apply a 4x4 affine transform to mesh vertex positions.
        /// Pure managed implementation — no native call overhead.
        /// </summary>
        public static MeshData Transform(MeshData mesh, XbimMatrix3D matrix)
        {
            var positions = mesh.Positions;
            var transformed = new float[positions.Length];

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            for (int i = 0; i < positions.Length; i += 3)
            {
                double x = positions[i];
                double y = positions[i + 1];
                double z = positions[i + 2];

                // XbimMatrix3D is row-major (row-vector * matrix): p' = p * M + offset
                double tx = x * matrix.M11 + y * matrix.M21 + z * matrix.M31 + matrix.OffsetX;
                double ty = x * matrix.M12 + y * matrix.M22 + z * matrix.M32 + matrix.OffsetY;
                double tz = x * matrix.M13 + y * matrix.M23 + z * matrix.M33 + matrix.OffsetZ;

                transformed[i] = (float)tx;
                transformed[i + 1] = (float)ty;
                transformed[i + 2] = (float)tz;

                if (tx < minX) minX = tx;
                if (ty < minY) minY = ty;
                if (tz < minZ) minZ = tz;
                if (tx > maxX) maxX = tx;
                if (ty > maxY) maxY = ty;
                if (tz > maxZ) maxZ = tz;
            }

            var bounds = new XbimRect3D(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ);
            return new MeshData(transformed, mesh.Indices, bounds);
        }

        internal static unsafe NativeManifoldMeshHandle CreateManifoldMesh(MeshData mesh)
        {
            fixed (float* pPos = mesh.Positions)
            fixed (uint* pIdx = mesh.Indices)
            {
                int rc = XbimGeometryNativeApi.xbim_manifold_mesh_create(
                    pPos, mesh.VertexCount,
                    pIdx, mesh.TriangleCount,
                    out var handle);

                return rc == 0 ? handle : null;
            }
        }

        internal static MeshData? ExtractMeshData(NativeManifoldMeshHandle handle)
        {
            int rc = XbimGeometryNativeApi.xbim_manifold_mesh_get_data(
                handle,
                out int numVerts, out int numTris,
                out IntPtr posPtr, out IntPtr idxPtr);

            if (rc != 0) return null;

            try
            {
                var positions = new float[numVerts * 3];
                var indices = new uint[numTris * 3];

                Marshal.Copy(posPtr, positions, 0, positions.Length);
                // Marshal.Copy doesn't have a uint[] overload, so copy as int[]
                var indicesInt = new int[indices.Length];
                Marshal.Copy(idxPtr, indicesInt, 0, indicesInt.Length);
                Buffer.BlockCopy(indicesInt, 0, indices, 0, indices.Length * sizeof(uint));

                // Get bounding box
                rc = XbimGeometryNativeApi.xbim_manifold_mesh_bounding_box(
                    handle,
                    out double minX, out double minY, out double minZ,
                    out double maxX, out double maxY, out double maxZ);

                XbimRect3D bounds;
                if (rc == 0)
                    bounds = new XbimRect3D(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ);
                else
                    bounds = ComputeBounds(positions);

                return new MeshData(positions, indices, bounds);
            }
            finally
            {
                NativeHandleMethods.xbim_buffer_free_float(posPtr);
                NativeHandleMethods.xbim_buffer_free_uint(idxPtr);
            }
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
