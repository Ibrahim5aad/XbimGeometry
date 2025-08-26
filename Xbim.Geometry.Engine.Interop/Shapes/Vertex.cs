using System;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;

namespace Xbim.Geometry.Engine.Interop.Shapes
{
    /// <summary>
    /// Implementation of <see cref="IXVertex"/> backed by a <see cref="NativeShapeHandle"/>
    /// containing a TopoDS_Vertex.
    /// </summary>
    internal class Vertex : Shape, IXVertex
    {
        internal Vertex(NativeShapeHandle handle) : base(handle)
        {
        }

        public double Tolerance
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_vertex_tolerance(Handle, out double tol);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get vertex tolerance: {XbimGeometryNativeApi.GetLastError()}");
                return tol;
            }
        }

        public IXPoint VertexGeometry
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_vertex_point(
                    Handle, out double x, out double y, out double z);
                if (result != 0)
                    throw new InvalidOperationException(
                        $"Failed to get vertex point: {XbimGeometryNativeApi.GetLastError()}");
                return new XPoint(x, y, z);
            }
        }
    }
}
