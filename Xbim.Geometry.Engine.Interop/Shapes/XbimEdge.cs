using System;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Represents an edge shape (TopoDS_Edge), implementing both the V6 <see cref="IXEdge"/>
    /// and the legacy <see cref="IXbimEdge"/> interfaces.
    /// </summary>
    internal class XbimEdge : XbimShape, IXEdge, IXbimEdge, IEquatable<IXbimEdge>
    {
        internal XbimEdge(NativeShapeHandle handle) : base(handle)
        {
        }

        public override XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimEdgeType;

        #region IXEdge

        public double Length
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_edge_length(Handle, out double length);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get edge length: {XbimGeometryNativeApi.GetLastError()}");
                return length;
            }
        }

        public double Tolerance
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_edge_tolerance(Handle, out double tol);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get edge tolerance: {XbimGeometryNativeApi.GetLastError()}");
                return tol;
            }
        }

        public IXCurve EdgeGeometry
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_edge_get_curve(
                    Handle, out var curveHandle, out _, out _);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get edge curve: {XbimGeometryNativeApi.GetLastError()}");
                return new XbimCurve(curveHandle, XCurveType.IfcLine);
            }
        }

        public IXVertex EdgeStart
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_edge_vertices(Handle, out var start, out var end);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get edge vertices: {XbimGeometryNativeApi.GetLastError()}");
                end?.Dispose();
                if (start == null || start.IsInvalid)
                    return null!;
                return new XbimVertex(start);
            }
        }

        public IXVertex EdgeEnd
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_edge_vertices(Handle, out var start, out var end);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get edge vertices: {XbimGeometryNativeApi.GetLastError()}");
                start?.Dispose();
                if (end == null || end.IsInvalid)
                    return null!;
                return new XbimVertex(end);
            }
        }

        #endregion

        #region IXbimEdge (explicit for clashing names)

        IXbimVertex IXbimEdge.EdgeStart
        {
            get
            {
                var vertexHandles = GetSubShapeHandles(XShapeType.Vertex);
                if (vertexHandles.Length == 0)
                    throw new XbimGeometryServiceException("Edge has no vertices.");
                return new XbimVertex(vertexHandles[0]);
            }
        }

        IXbimVertex IXbimEdge.EdgeEnd
        {
            get
            {
                var vertexHandles = GetSubShapeHandles(XShapeType.Vertex);
                if (vertexHandles.Length == 0)
                    throw new XbimGeometryServiceException("Edge has no vertices.");
                return new XbimVertex(vertexHandles[vertexHandles.Length > 1 ? 1 : 0]);
            }
        }

        IXbimCurve IXbimEdge.EdgeGeometry
        {
            get
            {
                return (XbimCurve)EdgeGeometry;
            }
        }

        public string ToBRep => BrepString();

        public bool Equals(IXbimEdge other)
        {
            if (other is XbimShape os)
                return IsEqual(os);
            return false;
        }

        #endregion
    }
}
