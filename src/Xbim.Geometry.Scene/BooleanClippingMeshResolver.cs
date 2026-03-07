using System;
using System.Runtime.InteropServices;
using Xbim.Common.Geometry;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Engine.Services;
using Xbim.Ifc4.Interfaces;
using Xbim.Tessellator;

namespace Xbim.Geometry.Scene
{
    /// <summary>
    /// Walks an IfcBooleanClippingResult CSG tree and resolves it to a triangle mesh
    /// using native mesh builders and Manifold mesh booleans. Works with native handles
    /// throughout to avoid intermediate managed array allocations. Falls back to null
    /// (triggering OCCT BRep fallback) for any unsupported operand type.
    /// </summary>
    internal class BooleanClippingMeshResolver
    {
        private readonly XbimTessellator _tessellator;

        public BooleanClippingMeshResolver(XbimTessellator tessellator)
        {
            _tessellator = tessellator;
        }

        public ManifoldMeshBooleanService.MeshData? TryResolve(IIfcBooleanClippingResult clipping)
        {
            try
            {
                using var body = ResolveToHandle(clipping.FirstOperand, XbimRect3D.Empty);
                if (body == null || body.IsInvalid) return null;

                var bodyBounds = GetHandleBounds(body);

                using var tool = ResolveToHandle(clipping.SecondOperand, bodyBounds);
                if (tool == null || tool.IsInvalid) return null;

                int rc = XbimGeometryNativeApi.xbim_manifold_boolean_cut(
                    body, tool, out var resultHandle);
                if (rc != 0) return null;

                using (resultHandle)
                    return ManifoldMeshBooleanService.ExtractMeshData(resultHandle);
            }
            catch
            {
                return null;
            }
        }

        private NativeManifoldMeshHandle ResolveToHandle(
            IIfcBooleanOperand operand, XbimRect3D bodyBounds)
        {
            try
            {
                if (operand is IIfcBooleanClippingResult nestedClipping)
                    return ResolveClippingToHandle(nestedClipping);

                if (operand is IIfcPolygonalBoundedHalfSpace polyBounded)
                    return ResolvePolygonalBoundedHalfSpace(polyBounded, bodyBounds);

                if (operand is IIfcHalfSpaceSolid halfSpace)
                    return ResolveHalfSpaceSolid(halfSpace, bodyBounds);

                if (operand is IIfcExtrudedAreaSolid extrusion
                    && !(operand is IIfcExtrudedAreaSolidTapered)
                    && ProfilePolygonBuilder.CanBuildPolygons(extrusion.SweptArea))
                    return ResolveExtrusion(extrusion);

                if (operand is IIfcRepresentationItem repItem && _tessellator.CanMesh(repItem))
                    return ResolveTessellatable(repItem);

                return null;
            }
            catch
            {
                return null;
            }
        }

        private NativeManifoldMeshHandle ResolveClippingToHandle(
            IIfcBooleanClippingResult clipping)
        {
            using var body = ResolveToHandle(clipping.FirstOperand, XbimRect3D.Empty);
            if (body == null || body.IsInvalid) return null;

            var bodyBounds = GetHandleBounds(body);

            using var tool = ResolveToHandle(clipping.SecondOperand, bodyBounds);
            if (tool == null || tool.IsInvalid) return null;

            int rc = XbimGeometryNativeApi.xbim_manifold_boolean_cut(
                body, tool, out var resultHandle);
            return rc == 0 ? resultHandle : null;
        }

        private NativeManifoldMeshHandle ResolveExtrusion(IIfcExtrudedAreaSolid extrusion)
        {
            double angTol = extrusion.Model.ModelFactors.DeflectionAngle;
            var polygons = ProfilePolygonBuilder.BuildPolygons(extrusion.SweptArea, angTol);

            var outer = polygons.Outer;
            if (extrusion.SweptArea is IIfcParameterizedProfileDef paramProfile
                && paramProfile.Position != null)
                outer = ProfilePolygonBuilder.ApplyProfilePosition(outer, paramProfile.Position);

            // Flatten outer contour to interleaved double array
            var flatOuter = new double[outer.Length * 2];
            for (int i = 0; i < outer.Length; i++)
            {
                flatOuter[i * 2] = outer[i].X;
                flatOuter[i * 2 + 1] = outer[i].Y;
            }

            // Flatten inner contours
            double[] flatInners = null;
            int[] innerSizes = null;
            int numInners = 0;

            if (polygons.Inners != null && polygons.Inners.Length > 0)
            {
                numInners = polygons.Inners.Length;
                innerSizes = new int[numInners];
                int totalInnerPts = 0;
                for (int h = 0; h < numInners; h++)
                {
                    innerSizes[h] = polygons.Inners[h].Length;
                    totalInnerPts += polygons.Inners[h].Length;
                }

                flatInners = new double[totalInnerPts * 2];
                int offset = 0;
                for (int h = 0; h < numInners; h++)
                {
                    var inner = polygons.Inners[h];
                    if (extrusion.SweptArea is IIfcParameterizedProfileDef pp && pp.Position != null)
                        inner = ProfilePolygonBuilder.ApplyProfilePosition(inner, pp.Position);

                    for (int i = 0; i < inner.Length; i++)
                    {
                        flatInners[offset++] = inner[i].X;
                        flatInners[offset++] = inner[i].Y;
                    }
                }
            }

            // Extrusion direction
            var dir = extrusion.ExtrudedDirection;
            double extDirX = dir.DirectionRatios[0];
            double extDirY = dir.DirectionRatios[1];
            double extDirZ = dir.DirectionRatios.Count > 2 ? dir.DirectionRatios[2] : 0;
            double depth = (double)extrusion.Depth;

            // Placement
            XbimGeometryNativeApi.XbimMat3x4 placementMat = default;
            bool hasPlacement = extrusion.Position != null;
            if (hasPlacement)
            {
                ExtrusionMeshBuilder.GetSolidAxes(extrusion.Position,
                    out double ox, out double oy, out double oz,
                    out double xx, out double xy, out double xz,
                    out double yx, out double yy, out double yz,
                    out double zx, out double zy, out double zz);
                placementMat = new XbimGeometryNativeApi.XbimMat3x4(
                    xx, yx, zx, ox,
                    xy, yy, zy, oy,
                    xz, yz, zz, oz);
            }

            // Pin arrays and build the params struct
            var outerPin = GCHandle.Alloc(flatOuter, GCHandleType.Pinned);
            var innersPin = flatInners != null ? GCHandle.Alloc(flatInners, GCHandleType.Pinned) : default;
            var sizesPin = innerSizes != null ? GCHandle.Alloc(innerSizes, GCHandleType.Pinned) : default;
            GCHandle placementPin = default;
            if (hasPlacement)
                placementPin = GCHandle.Alloc(placementMat, GCHandleType.Pinned);

            try
            {
                var p = new XbimGeometryNativeApi.XbimExtrusionParams
                {
                    Profile = new XbimGeometryNativeApi.XbimProfileContours
                    {
                        OuterPoints = outerPin.AddrOfPinnedObject(),
                        OuterCount = outer.Length,
                        InnerPoints = innersPin.IsAllocated ? innersPin.AddrOfPinnedObject() : IntPtr.Zero,
                        InnerSizes = sizesPin.IsAllocated ? sizesPin.AddrOfPinnedObject() : IntPtr.Zero,
                        NumInners = numInners
                    },
                    ExtDirX = extDirX,
                    ExtDirY = extDirY,
                    ExtDirZ = extDirZ,
                    Depth = depth,
                    Placement = placementPin.IsAllocated ? placementPin.AddrOfPinnedObject() : IntPtr.Zero
                };

                int rc = XbimGeometryNativeApi.xbim_manifold_mesh_extrude(in p, out var handle);
                return rc == 0 ? handle : null;
            }
            finally
            {
                outerPin.Free();
                if (innersPin.IsAllocated) innersPin.Free();
                if (sizesPin.IsAllocated) sizesPin.Free();
                if (placementPin.IsAllocated) placementPin.Free();
            }
        }

        private NativeManifoldMeshHandle ResolveTessellatable(IIfcRepresentationItem repItem)
        {
            var mesh = _tessellator.MeshToTriangulatedMesh(repItem);
            var raw = XbimTessellator.ExtractRawMesh(mesh);
            var meshData = new ManifoldMeshBooleanService.MeshData(
                raw.Positions, raw.Indices, raw.Bounds);
            return ManifoldMeshBooleanService.CreateManifoldMesh(meshData);
        }

        private NativeManifoldMeshHandle ResolveHalfSpaceSolid(
            IIfcHalfSpaceSolid halfSpace, XbimRect3D bodyBounds)
        {
            if (!(halfSpace.BaseSurface is IIfcPlane plane))
                return null;

            ExtrusionMeshBuilder.GetSolidAxes(plane.Position,
                out double ox, out double oy, out double oz,
                out double ax, out double ay, out double az,
                out double _, out double _, out double _,
                out double nx, out double ny, out double nz);

            var p = new XbimGeometryNativeApi.XbimHalfSpaceParams
            {
                PlaneOx = ox, PlaneOy = oy, PlaneOz = oz,
                PlaneNx = nx, PlaneNy = ny, PlaneNz = nz,
                PlaneAx = ax, PlaneAy = ay, PlaneAz = az,
                AgreementFlag = halfSpace.AgreementFlag ? 1 : 0,
                BodyBBox = new XbimGeometryNativeApi.XbimBBox(bodyBounds)
            };

            int rc = XbimGeometryNativeApi.xbim_manifold_mesh_halfspace(in p, out var handle);
            return rc == 0 ? handle : null;
        }

        private NativeManifoldMeshHandle ResolvePolygonalBoundedHalfSpace(
            IIfcPolygonalBoundedHalfSpace polyBounded, XbimRect3D bodyBounds)
        {
            if (!(polyBounded.BaseSurface is IIfcPlane plane))
                return null;
            if (!(polyBounded.PolygonalBoundary is IIfcPolyline polyline))
                return null;

            var points = polyline.Points;
            if (points == null || points.Count < 3)
                return null;

            int n = points.Count;
            if (n > 3)
            {
                var first = points[0];
                var last = points[n - 1];
                double ddx = first.Coordinates[0] - last.Coordinates[0];
                double ddy = first.Coordinates[1] - last.Coordinates[1];
                if (ddx * ddx + ddy * ddy < 1e-10)
                    n--;
            }

            var polygon2D = new double[n * 2];
            for (int i = 0; i < n; i++)
            {
                polygon2D[i * 2] = points[i].Coordinates[0];
                polygon2D[i * 2 + 1] = points[i].Coordinates[1];
            }

            ExtrusionMeshBuilder.GetSolidAxes(plane.Position,
                out double planeOx, out double planeOy, out double planeOz,
                out double _, out double _, out double _,
                out double _, out double _, out double _,
                out double planeNx, out double planeNy, out double planeNz);

            ExtrusionMeshBuilder.GetSolidAxes(polyBounded.Position,
                out double bOx, out double bOy, out double bOz,
                out double bXx, out double bXy, out double bXz,
                out double bYx, out double bYy, out double bYz,
                out double bZx, out double bZy, out double bZz);

            var boundaryMat = new XbimGeometryNativeApi.XbimMat3x4(
                bXx, bYx, bZx, bOx,
                bXy, bYy, bZy, bOy,
                bXz, bYz, bZz, bOz);

            var polyPin = GCHandle.Alloc(polygon2D, GCHandleType.Pinned);
            var bpPin = GCHandle.Alloc(boundaryMat, GCHandleType.Pinned);

            try
            {
                var p = new XbimGeometryNativeApi.XbimPolyHalfSpaceParams
                {
                    PlaneOx = planeOx, PlaneOy = planeOy, PlaneOz = planeOz,
                    PlaneNx = planeNx, PlaneNy = planeNy, PlaneNz = planeNz,
                    AgreementFlag = polyBounded.AgreementFlag ? 1 : 0,
                    PolygonPoints = polyPin.AddrOfPinnedObject(),
                    PolygonCount = n,
                    BoundaryPlacement = bpPin.AddrOfPinnedObject(),
                    BodyBBox = new XbimGeometryNativeApi.XbimBBox(bodyBounds)
                };

                int rc = XbimGeometryNativeApi.xbim_manifold_mesh_halfspace_polygonal(
                    in p, out var handle);
                return rc == 0 ? handle : null;
            }
            finally
            {
                polyPin.Free();
                bpPin.Free();
            }
        }

        private static XbimRect3D GetHandleBounds(NativeManifoldMeshHandle handle)
        {
            int rc = XbimGeometryNativeApi.xbim_manifold_mesh_bounding_box(
                handle,
                out double minX, out double minY, out double minZ,
                out double maxX, out double maxY, out double maxZ);

            if (rc != 0) return XbimRect3D.Empty;
            return new XbimRect3D(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ);
        }
    }
}
