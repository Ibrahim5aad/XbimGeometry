using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Represents a shell shape (TopoDS_Shell), implementing both the V6 <see cref="IXShell"/>
    /// and the legacy <see cref="IXbimShell"/> interfaces.
    /// </summary>
    internal class XbimShell : XbimShape, IXShell, IXbimShell, IEquatable<IXbimShell>
    {
        internal XbimShell(NativeShapeHandle handle) : base(handle)
        {
        }

        public override XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimShellType;

        #region IXShell

        public double SurfaceArea
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_shape_surface_area(Handle, out double area);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to compute surface area: {XbimGeometryNativeApi.GetLastError()}");
                return area;
            }
        }

        public IXFace[] Faces
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Face);
                return handles.Select(h => (IXFace)new XbimFace(h)).ToArray();
            }
        }

        #endregion

        #region IXbimShell

        public bool IsPolyhedron => false;

        public bool CanCreateSolid() => IsClosed;

        public IXbimSolid CreateSolid()
        {
            int result = XbimGeometryNativeApi.xbim_shell_make_solid(
                NativeContextHandle.NullHandle,
                Handle,
                out var solidHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to create solid from shell: {XbimGeometryNativeApi.GetLastError()}");

            return new XbimSolid(solidHandle);
        }

        IXbimFaceSet IXbimShell.Faces
        {
            get
            {
                var v6Faces = Faces;
                var faces = v6Faces.Select(f => (IXbimFace)f).ToArray();
                return new XbimFaceSet(faces);
            }
        }

        IXbimEdgeSet IXbimShell.Edges
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Edge);
                var edges = handles.Select(h => (IXbimEdge)new XbimEdge(h)).ToArray();
                return new XbimEdgeSet(edges);
            }
        }

        IXbimVertexSet IXbimShell.Vertices
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Vertex);
                var verts = handles.Select(h => (IXbimVertex)new XbimVertex(h)).ToArray();
                return new XbimVertexSet(verts);
            }
        }

        public IXbimGeometryObjectSet Cut(IXbimSolid toCut, double tolerance, ILogger logger = null)
            => PerformBoolean(XbimGeometryNativeApi.xbim_boolean_cut, toCut, tolerance);

        public IXbimGeometryObjectSet Cut(IXbimSolidSet toCut, double tolerance, ILogger logger = null)
            => PerformBooleanChained(XbimGeometryNativeApi.xbim_boolean_cut, toCut, tolerance);

        public IXbimGeometryObjectSet Union(IXbimSolid toUnion, double tolerance, ILogger logger = null)
            => PerformBoolean(XbimGeometryNativeApi.xbim_boolean_union, toUnion, tolerance);

        public IXbimGeometryObjectSet Union(IXbimSolidSet toUnion, double tolerance, ILogger logger = null)
            => PerformBooleanChained(XbimGeometryNativeApi.xbim_boolean_union, toUnion, tolerance);

        public IXbimGeometryObjectSet Intersection(IXbimSolid toIntersect, double tolerance, ILogger logger = null)
            => PerformBoolean(XbimGeometryNativeApi.xbim_boolean_intersect, toIntersect, tolerance);

        public IXbimGeometryObjectSet Intersection(IXbimSolidSet toIntersect, double tolerance, ILogger logger = null)
            => PerformBooleanChained(XbimGeometryNativeApi.xbim_boolean_intersect, toIntersect, tolerance);

        public IXbimFaceSet Section(IXbimFace toSection, double tolerance, ILogger logger = null)
        {
            if (!IsValid || !toSection.IsValid)
                return new XbimFaceSet(Array.Empty<IXbimFace>());

            var faceHandle = ((XbimFace)toSection).Handle;
            int result = XbimGeometryNativeApi.xbim_boolean_section(
                NativeContextHandle.NullHandle,
                Handle, faceHandle, tolerance,
                out var resultHandle);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Section operation failed: {XbimGeometryNativeApi.GetLastError()}");

            var shape = (XbimShape)NativeShapeWrapper.WrapShape(resultHandle);
            var faceHandles = shape.GetSubShapeHandles(XShapeType.Face);
            var faces = faceHandles.Select(h => (IXbimFace)new XbimFace(h)).ToArray();
            return new XbimFaceSet(faces);
        }

        public void SaveAsBrep(string fileName) => WriteBrep(fileName);

        public string ToBRep => BrepString();

        public bool Equals(IXbimShell other)
        {
            if (other is XbimShape os)
                return IsEqual(os);
            return false;
        }

        #endregion

        #region Boolean helpers

        private delegate int BooleanOp(
            NativeContextHandle ctx,
            NativeShapeHandle body,
            NativeShapeHandle tool,
            double fuzzyTolerance,
            out int outHasWarnings,
            out NativeShapeHandle outHandle);

        private IXbimGeometryObjectSet PerformBoolean(BooleanOp op, IXbimSolid tool, double tolerance)
        {
            var toolHandle = ((XbimShape)tool).Handle;
            int result = op(
                NativeContextHandle.NullHandle,
                Handle, toolHandle, tolerance,
                out _, out var resultHandle);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Boolean operation failed: {XbimGeometryNativeApi.GetLastError()}");

            return BuildObjectSet(resultHandle);
        }

        private IXbimGeometryObjectSet PerformBooleanChained(BooleanOp op, IXbimSolidSet tools, double tolerance)
        {
            NativeShapeHandle bodyHandle = Handle;
            foreach (var tool in tools)
            {
                var toolHandle = ((XbimShape)tool).Handle;
                int result = op(
                    NativeContextHandle.NullHandle,
                    bodyHandle, toolHandle, tolerance,
                    out _, out var resultHandle);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Boolean operation failed: {XbimGeometryNativeApi.GetLastError()}");
                bodyHandle = resultHandle;
            }

            return BuildObjectSet(bodyHandle);
        }

        private static XbimGeometryObjectSet BuildObjectSet(NativeShapeHandle resultHandle)
        {
            var shape = (XbimShape)NativeShapeWrapper.WrapShape(resultHandle);
            var solidHandles = shape.GetSubShapeHandles(XShapeType.Solid);
            if (solidHandles.Length > 0)
            {
                var items = solidHandles.Select(h => (IXbimGeometryObject)new XbimSolid(h));
                return new XbimGeometryObjectSet(items);
            }

            return new XbimGeometryObjectSet(new[] { (IXbimGeometryObject)shape });
        }

        #endregion
    }
}
