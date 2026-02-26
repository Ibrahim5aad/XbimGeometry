using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Primitives
{
    /// <summary>
    /// Wraps a native 2D curve handle (Geom2d_Curve), implementing <see cref="IXCurve"/>
    /// and <see cref="IXbimCurve"/> interfaces.
    /// Point evaluation returns 2D XY coordinates; accessing Z on <see cref="IXCurve"/> results throws.
    /// The <see cref="IXbimCurve"/> surface uses <see cref="XbimPoint3D"/> with Z=0.
    /// </summary>
    internal class XbimCurve2d : NativeOwner<NativeCurve2dHandle>, IXCurve, IXbimCurve
    {
        private readonly XCurveType _curveType;

        internal XbimCurve2d(NativeCurve2dHandle handle, XCurveType curveType) : base(handle)
        {
            _curveType = curveType;
        }

        #region IXCurve

        public XCurveType CurveType => _curveType;

        public bool Is3d => false;

        public double Length
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_curve2d_length(Handle, out double length);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to compute 2D curve length: {XbimGeometryNativeApi.GetLastError()}");
                return length;
            }
        }

        public double FirstParameter
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_curve2d_parameters(Handle, out double first, out _);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get 2D curve parameters: {XbimGeometryNativeApi.GetLastError()}");
                return first;
            }
        }

        public double LastParameter
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_curve2d_parameters(Handle, out _, out double last);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get 2D curve parameters: {XbimGeometryNativeApi.GetLastError()}");
                return last;
            }
        }

        public IXPoint GetPoint(double uParam)
        {
            int result = XbimGeometryNativeApi.xbim_curve2d_value(
                Handle, uParam, out double x, out double y);
            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to evaluate 2D curve at u={uParam}: {XbimGeometryNativeApi.GetLastError()}");
            return new XPoint(x, y);
        }

        public IXPoint GetFirstDerivative(double uParam, out IXDirection direction)
        {
            int result = XbimGeometryNativeApi.xbim_curve2d_d1(
                Handle, uParam,
                out double px, out double py,
                out double dx, out double dy);
            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to evaluate 2D curve D1 at u={uParam}: {XbimGeometryNativeApi.GetLastError()}");

            double mag = Math.Sqrt(dx * dx + dy * dy);
            if (mag > 1e-15)
                direction = new XDirection(dx, dy);
            else
                direction = new XDirection(1, 0);

            return new XPoint(px, py);
        }

        public IXPoint GetSecondDerivative(double uParam, out IXDirection direction, out IXDirection normal)
        {
            int result = XbimGeometryNativeApi.xbim_curve2d_d2(
                Handle, uParam,
                out double px, out double py,
                out double d1x, out double d1y,
                out double d2x, out double d2y);
            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to evaluate 2D curve D2 at u={uParam}: {XbimGeometryNativeApi.GetLastError()}");

            double mag1 = Math.Sqrt(d1x * d1x + d1y * d1y);
            if (mag1 > 1e-15)
                direction = new XDirection(d1x, d1y);
            else
                direction = new XDirection(1, 0);

            double mag2 = Math.Sqrt(d2x * d2x + d2y * d2y);
            if (mag2 > 1e-15)
                normal = new XDirection(d2x, d2y);
            else
                normal = new XDirection(0, 0);

            return new XPoint(px, py);
        }

        #endregion

        #region IXbimCurve

        XbimGeometryObjectType IXbimGeometryObject.GeometryType => XbimGeometryObjectType.XbimCurveType;

        bool IXbimGeometryObject.IsValid => Handle != null && !Handle.IsInvalid && !Handle.IsClosed;

        bool IXbimGeometryObject.IsSet => false;

        XbimRect3D IXbimGeometryObject.BoundingBox => XbimRect3D.Empty;

        object IXbimGeometryObject.Tag { get; set; }

        bool IXbimCurve.Is3D => false;

        bool IXbimCurve.IsClosed
        {
            get
            {
                var s = GetPoint(FirstParameter);
                var e = GetPoint(LastParameter);
                double dx = s.X - e.X, dy = s.Y - e.Y;
                return Math.Sqrt(dx * dx + dy * dy) < 1e-7;
            }
        }

        string IXbimCurve.ToBRep => string.Empty;

        XbimPoint3D IXbimCurve.Start
        {
            get
            {
                var pt = GetPoint(FirstParameter);
                return new XbimPoint3D(pt.X, pt.Y, 0);
            }
        }

        XbimPoint3D IXbimCurve.End
        {
            get
            {
                var pt = GetPoint(LastParameter);
                return new XbimPoint3D(pt.X, pt.Y, 0);
            }
        }

        double IXbimCurve.GetParameter(XbimPoint3D point, double tolerance)
            => throw new NotSupportedException("GetParameter is not supported for 2D curves.");

        XbimPoint3D IXbimCurve.GetPoint(double parameter)
        {
            var pt = GetPoint(parameter);
            return new XbimPoint3D(pt.X, pt.Y, 0);
        }

        IEnumerable<XbimPoint3D> IXbimCurve.Intersections(IXbimCurve intersector, double tolerance, ILogger logger)
            => Enumerable.Empty<XbimPoint3D>();

        IXbimGeometryObject IXbimGeometryObject.Transform(XbimMatrix3D matrix3D) => this;

        IXbimGeometryObject IXbimGeometryObject.TransformShallow(XbimMatrix3D matrix3D) => this;

        #endregion
    }
}
