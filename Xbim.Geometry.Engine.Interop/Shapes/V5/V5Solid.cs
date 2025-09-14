using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Shapes.V5
{
    /// <summary>
    /// Adapts a V6 <see cref="Solid"/> to the legacy <see cref="IXbimSolid"/> interface.
    /// </summary>
    internal class V5Solid : V5Shape, IXbimSolid, IEquatable<IXbimSolid>
    {
        private readonly Solid _solid;

        internal V5Solid(Solid solid) : base(solid)
        {
            _solid = solid;
        }

        public override XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimSolidType;

        public double Volume => _solid.Volume;

        public double SurfaceArea
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_shape_surface_area(
                    _solid.Handle, out double area);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to compute surface area: {XbimGeometryNativeApi.GetLastError()}");
                return area;
            }
        }

        public bool IsPolyhedron => false;

        public IXbimShellSet Shells
        {
            get
            {
                var v6Shells = _solid.Shells;
                var v5Shells = v6Shells.Select(s => (IXbimShell)new V5Shell((Shell)s)).ToArray();
                return new V5ShellSet(v5Shells);
            }
        }

        public IXbimFaceSet Faces
        {
            get
            {
                var handles = Inner.GetSubShapeHandles(XShapeType.Face);
                var v5Faces = handles.Select(h => (IXbimFace)new V5Face(new Face(h))).ToArray();
                return new V5FaceSet(v5Faces);
            }
        }

        public IXbimEdgeSet Edges
        {
            get
            {
                var handles = Inner.GetSubShapeHandles(XShapeType.Edge);
                var v5Edges = handles.Select(h => (IXbimEdge)new V5Edge(new Edge(h))).ToArray();
                return new V5EdgeSet(v5Edges);
            }
        }

        public IXbimVertexSet Vertices
        {
            get
            {
                var handles = Inner.GetSubShapeHandles(XShapeType.Vertex);
                var v5Verts = handles.Select(h => (IXbimVertex)new V5Vertex(new Vertex(h))).ToArray();
                return new V5VertexSet(v5Verts);
            }
        }

        public IXbimSolidSet Cut(IXbimSolid toCut, double tolerance, ILogger logger = null)
            => PerformBoolean(XbimGeometryNativeApi.xbim_boolean_cut, toCut, tolerance);

        public IXbimSolidSet Cut(IXbimSolidSet toCut, double tolerance, ILogger logger = null)
            => PerformBooleanChained(XbimGeometryNativeApi.xbim_boolean_cut, toCut, tolerance);

        public IXbimSolidSet Union(IXbimSolid toUnion, double tolerance, ILogger logger = null)
            => PerformBoolean(XbimGeometryNativeApi.xbim_boolean_union, toUnion, tolerance);

        public IXbimSolidSet Union(IXbimSolidSet toUnion, double tolerance, ILogger logger = null)
            => PerformBooleanChained(XbimGeometryNativeApi.xbim_boolean_union, toUnion, tolerance);

        public IXbimSolidSet Intersection(IXbimSolid toIntersect, double tolerance, ILogger logger = null)
            => PerformBoolean(XbimGeometryNativeApi.xbim_boolean_intersect, toIntersect, tolerance);

        public IXbimSolidSet Intersection(IXbimSolidSet toIntersect, double tolerance, ILogger logger = null)
            => PerformBooleanChained(XbimGeometryNativeApi.xbim_boolean_intersect, toIntersect, tolerance);

        public IXbimFaceSet Section(IXbimFace toSection, double tolerance, ILogger logger = null)
        {
            if (!IsValid || !toSection.IsValid)
                return new V5FaceSet(Array.Empty<IXbimFace>());

            var faceHandle = ((V5Face)toSection).Inner.Handle;
            int result = XbimGeometryNativeApi.xbim_boolean_section(
                NativeContextHandle.NullHandle,
                _solid.Handle, faceHandle, tolerance,
                out var resultHandle);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Section operation failed: {XbimGeometryNativeApi.GetLastError()}");

            var shape = (Shape)NativeShapeWrapper.WrapShape(resultHandle);
            var faceHandles = shape.GetSubShapeHandles(XShapeType.Face);
            var v5Faces = faceHandles.Select(h => (IXbimFace)new V5Face(new Face(h))).ToArray();
            return new V5FaceSet(v5Faces);
        }

        public void SaveAsBrep(string fileName) => ((Shape)Inner).WriteBrep(fileName);

        public string ToBRep => Inner.BrepString();

        public bool Equals(IXbimSolid other)
        {
            if (other is V5Solid v5 && Inner is Shape s && v5.Inner is Shape os)
                return s.IsEqual(os);
            return false;
        }

        #region Boolean helpers

        private delegate int BooleanOp(
            NativeContextHandle ctx,
            NativeShapeHandle body,
            NativeShapeHandle tool,
            double fuzzyTolerance,
            out int outHasWarnings,
            out NativeShapeHandle outHandle);

        private IXbimSolidSet PerformBoolean(BooleanOp op, IXbimSolid tool, double tolerance)
        {
            var toolHandle = ((V5Solid)tool).Inner.Handle;
            int result = op(
                NativeContextHandle.NullHandle,
                _solid.Handle, toolHandle, tolerance,
                out _, out var resultHandle);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Boolean operation failed: {XbimGeometryNativeApi.GetLastError()}");

            var shape = (Shape)NativeShapeWrapper.WrapShape(resultHandle);
            return new V5SolidSet(shape);
        }

        private IXbimSolidSet PerformBooleanChained(BooleanOp op, IXbimSolidSet tools, double tolerance)
        {
            NativeShapeHandle bodyHandle = Inner.Handle;
            foreach (var tool in tools)
            {
                var toolHandle = ((V5Solid)tool).Inner.Handle;
                int result = op(
                    NativeContextHandle.NullHandle,
                    bodyHandle, toolHandle, tolerance,
                    out _, out var resultHandle);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Boolean operation failed: {XbimGeometryNativeApi.GetLastError()}");
                bodyHandle = resultHandle;
            }

            var shape = (Shape)NativeShapeWrapper.WrapShape(bodyHandle);
            return new V5SolidSet(shape);
        }

        #endregion
    }
}
