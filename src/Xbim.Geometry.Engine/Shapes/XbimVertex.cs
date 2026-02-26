using System;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Engine.Primitives;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Shapes
{
    /// <summary>
    /// Represents a vertex shape (TopoDS_Vertex), implementing <see cref="IXVertex"/>
    /// and <see cref="IXbimVertex"/> interfaces.
    /// </summary>
    internal class XbimVertex : XbimShape, IXVertex, IXbimVertex, IEquatable<IXbimVertex>
    {
        internal XbimVertex(NativeShapeHandle handle) : base(handle)
        {
        }

        public override XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimVertexType;

        #region IXVertex

        public double Tolerance
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_vertex_tolerance(Handle, out double tol);
                if (result != 0)
                    throw new XbimGeometryServiceException(
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
                    throw new XbimGeometryServiceException(
                        $"Failed to get vertex point: {XbimGeometryNativeApi.GetLastError()}");
                return new XPoint(x, y, z);
            }
        }

        #endregion

        #region IXbimVertex (explicit for clashing name)

        XbimPoint3D IXbimVertex.VertexGeometry
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_vertex_point(
                    Handle, out double x, out double y, out double z);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get vertex point: {XbimGeometryNativeApi.GetLastError()}");
                return new XbimPoint3D(x, y, z);
            }
        }

        public string ToBRep => BrepString();

        public bool Equals(IXbimVertex other)
        {
            if (other is XbimShape os)
                return IsEqual(os);
            return false;
        }

        #endregion
    }
}
