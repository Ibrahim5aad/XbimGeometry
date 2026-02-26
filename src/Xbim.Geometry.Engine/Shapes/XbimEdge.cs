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
    /// Represents an edge shape (TopoDS_Edge), implementing <see cref="IXEdge"/>
    /// and <see cref="IXbimEdge"/> interfaces.
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
                // Null curve for degenerate edges (e.g. vertex loop at cone apex)
                if (result != 0)
                    return null;

                int propsResult = XbimGeometryNativeApi.xbim_curve_get_elementary_props(
                    curveHandle,
                    out int curveTypeVal,
                    out double ox, out double oy, out double oz,
                    out double dx, out double dy, out double dz,
                    out double xx, out double xy, out double xz,
                    out double r1, out double r2);

                if (propsResult != 0)
                    return new XbimCurve(curveHandle, XCurveType.IfcCurve);

                var curveType = (XCurveType)curveTypeVal;
                switch (curveType)
                {
                    case XCurveType.IfcLine:
                        return new XbimLine3d(curveHandle,
                            new XPoint(ox, oy, oz),
                            new XVector(dx, dy, dz),
                            1.0);
                    case XCurveType.IfcCircle:
                        return new XbimCircle3d(curveHandle, r1,
                            new XAxis2Placement3d(
                                new XPoint(ox, oy, oz),
                                new XDirection(dx, dy, dz),
                                new XDirection(xx, xy, xz)));
                    case XCurveType.IfcEllipse:
                        return new XbimEllipse3d(curveHandle, r1, r2,
                            new XAxis2Placement3d(
                                new XPoint(ox, oy, oz),
                                new XDirection(dx, dy, dz),
                                new XDirection(xx, xy, xz)));
                    default:
                        return new XbimCurve(curveHandle, curveType);
                }
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

        #region IXbimEdge

        IXbimVertex IXbimEdge.EdgeStart => EdgeStart as IXbimVertex;

        IXbimVertex IXbimEdge.EdgeEnd => EdgeEnd as IXbimVertex;

        IXbimCurve IXbimEdge.EdgeGeometry => EdgeGeometry as IXbimCurve;

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
