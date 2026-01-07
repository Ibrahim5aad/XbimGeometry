using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Represents a solid shape (TopoDS_Solid), implementing both the V6 <see cref="IXSolid"/>
    /// and the legacy <see cref="IXbimSolid"/> interfaces.
    /// </summary>
    internal class XbimSolid : XbimShape, IXSolid, IXbimSolid, IEquatable<IXbimSolid>
    {
        internal XbimSolid(NativeShapeHandle handle) : base(handle)
        {
        }

        public override XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimSolidType;

        #region IXSolid

        public double Volume
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_shape_volume(Handle, out double volume);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to compute volume: {XbimGeometryNativeApi.GetLastError()}");
                return volume;
            }
        }

        public IXShell[] Shells
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Shell);
                return handles.Select(h => new XbimShell(h)).ToArray();
            }
        }

        #endregion

        #region IXbimSolid

        public double SurfaceArea
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_shape_surface_area(Handle, out double area);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to compute surface area: {XbimGeometryNativeApi.GetLastError()}");
                return area;
            }
        }

        public bool IsPolyhedron => false;

        IXbimShellSet IXbimSolid.Shells
        {
            get
            {
                var v6Shells = Shells;
                var shells = v6Shells.Select(s => (IXbimShell)s).ToArray();
                return new XbimShellSet(shells);
            }
        }

        IXbimFaceSet IXbimSolid.Faces
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Face);
                var faces = handles.Select(h => (IXbimFace)new XbimFace(h)).ToArray();
                return new XbimFaceSet(faces);
            }
        }

        IXbimEdgeSet IXbimSolid.Edges
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Edge);
                var edges = handles.Select(h => (IXbimEdge)new XbimEdge(h)).ToArray();
                return new XbimEdgeSet(edges);
            }
        }

        IXbimVertexSet IXbimSolid.Vertices
        {
            get
            {
                var handles = GetSubShapeHandles(XShapeType.Vertex);
                var verts = handles.Select(h => (IXbimVertex)new XbimVertex(h)).ToArray();
                return new XbimVertexSet(verts);
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
                return new XbimFaceSet(Array.Empty<IXbimFace>());

            var faceHandle = ((XbimFace)toSection).Handle;
            int result = XbimGeometryNativeApi.xbim_boolean_section(
                NativeContextHandle.NullHandle,
                Handle, faceHandle, tolerance,
                out var resultHandle);
            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Section operation failed: {XbimGeometryNativeApi.GetLastError()}");

            var shape = (XbimShape)NativeShapeWrapper.WrapShape(resultHandle);
            var faceHandles = shape.GetSubShapeHandles(XShapeType.Face);
            var faces = faceHandles.Select(h => (IXbimFace)new XbimFace(h)).ToArray();
            return new XbimFaceSet(faces);
        }

        public void SaveAsBrep(string fileName) => WriteBrep(fileName);

        public string ToBRep => BrepString();

        public bool Equals(IXbimSolid other)
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

        private IXbimSolidSet PerformBoolean(BooleanOp op, IXbimSolid tool, double tolerance)
        {
            var toolHandle = ((XbimShape)tool).Handle;
            int result = op(
                NativeContextHandle.NullHandle,
                Handle, toolHandle, tolerance,
                out _, out var resultHandle);
            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Boolean operation failed: {XbimGeometryNativeApi.GetLastError()}");

            var shape = (XbimShape)NativeShapeWrapper.WrapShape(resultHandle);
            return new XbimSolidSet(shape);
        }

        private IXbimSolidSet PerformBooleanChained(BooleanOp op, IXbimSolidSet tools, double tolerance)
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
                    throw new XbimGeometryServiceException(
                        $"Boolean operation failed: {XbimGeometryNativeApi.GetLastError()}");
                bodyHandle = resultHandle;
            }

            var shape = (XbimShape)NativeShapeWrapper.WrapShape(bodyHandle);
            return new XbimSolidSet(shape);
        }

        #endregion
    }
}
