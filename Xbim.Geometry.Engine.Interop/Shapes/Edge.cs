using System;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Implementation of <see cref="IXEdge"/> backed by a <see cref="NativeShapeHandle"/>
    /// containing a TopoDS_Edge.
    /// </summary>
    internal class Edge : Shape, IXEdge
    {
        internal Edge(NativeShapeHandle handle) : base(handle)
        {
        }

        public double Length
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_edge_length(Handle, out double length);
                if (result != 0)
                    throw new InvalidOperationException(
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
                    throw new InvalidOperationException(
                        $"Failed to get edge tolerance: {XbimGeometryNativeApi.GetLastError()}");
                return tol;
            }
        }

        public IXCurve EdgeGeometry
        {
            get
            {
                throw new NotImplementedException(
                    "Edge geometry extraction requires curve query infrastructure (CURVE phase).");
            }
        }

        public IXVertex EdgeStart
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_edge_vertices(Handle, out var start, out var end);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get edge vertices: {XbimGeometryNativeApi.GetLastError()}");
                end?.Dispose();
                if (start == null || start.IsInvalid)
                    return null!;
                return new Vertex(start);
            }
        }

        public IXVertex EdgeEnd
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_edge_vertices(Handle, out var start, out var end);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get edge vertices: {XbimGeometryNativeApi.GetLastError()}");
                start?.Dispose();
                if (end == null || end.IsInvalid)
                    return null!;
                return new Vertex(end);
            }
        }
    }
}
