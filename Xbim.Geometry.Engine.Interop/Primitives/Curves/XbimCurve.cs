using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Wraps a native curve handle (Geom_Curve), implementing both the V6 <see cref="IXCurve"/>
    /// and the legacy <see cref="IXbimCurve"/> interfaces.
    /// </summary>
    internal class XbimCurve : NativeOwner<NativeCurveHandle>, IXCurve, IXbimCurve
    {
        private readonly XCurveType _curveType;

        internal XbimCurve(NativeCurveHandle handle, XCurveType curveType) : base(handle)
        {
            _curveType = curveType;
        }

        #region IXCurve

        public XCurveType CurveType => _curveType;

        public bool Is3d => true;

        public double Length
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_curve_length(Handle, out double length);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to compute curve length: {XbimGeometryNativeApi.GetLastError()}");
                return length;
            }
        }

        public double FirstParameter
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_curve_parameters(Handle, out double first, out _);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get curve parameters: {XbimGeometryNativeApi.GetLastError()}");
                return first;
            }
        }

        public double LastParameter
        {
            get
            {
                int result = XbimGeometryNativeApi.xbim_curve_parameters(Handle, out _, out double last);
                if (result != 0)
                    throw new XbimGeometryServiceException(
                        $"Failed to get curve parameters: {XbimGeometryNativeApi.GetLastError()}");
                return last;
            }
        }

        public IXPoint GetPoint(double uParam)
        {
            int result = XbimGeometryNativeApi.xbim_curve_value(
                Handle, uParam, out double x, out double y, out double z);
            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to evaluate curve at u={uParam}: {XbimGeometryNativeApi.GetLastError()}");
            return new XPoint(x, y, z);
        }

        public IXPoint GetFirstDerivative(double uParam, out IXDirection direction)
        {
            int result = XbimGeometryNativeApi.xbim_curve_d1(
                Handle, uParam,
                out double px, out double py, out double pz,
                out double dx, out double dy, out double dz);
            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to evaluate curve D1 at u={uParam}: {XbimGeometryNativeApi.GetLastError()}");

            double mag = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (mag > 1e-15)
                direction = new XDirection(dx / mag, dy / mag, dz / mag);
            else
                direction = new XDirection(0, 0, 1);

            return new XPoint(px, py, pz);
        }

        public IXPoint GetSecondDerivative(double uParam, out IXDirection direction, out IXDirection normal)
        {
            int result = XbimGeometryNativeApi.xbim_curve_d2(
                Handle, uParam,
                out double px, out double py, out double pz,
                out double d1x, out double d1y, out double d1z,
                out double d2x, out double d2y, out double d2z);
            if (result != 0)
                throw new XbimGeometryServiceException(
                    $"Failed to evaluate curve D2 at u={uParam}: {XbimGeometryNativeApi.GetLastError()}");

            double mag1 = Math.Sqrt(d1x * d1x + d1y * d1y + d1z * d1z);
            if (mag1 > 1e-15)
                direction = new XDirection(d1x / mag1, d1y / mag1, d1z / mag1);
            else
                direction = new XDirection(0, 0, 1);

            double mag2 = Math.Sqrt(d2x * d2x + d2y * d2y + d2z * d2z);
            if (mag2 > 1e-15)
                normal = new XDirection(d2x / mag2, d2y / mag2, d2z / mag2);
            else
                normal = new XDirection(0, 0, 1);

            return new XPoint(px, py, pz);
        }

        #endregion

        #region IXbimCurve

        XbimGeometryObjectType IXbimGeometryObject.GeometryType => XbimGeometryObjectType.XbimCurveType;

        bool IXbimGeometryObject.IsValid => true;

        bool IXbimGeometryObject.IsSet => false;

        XbimRect3D IXbimGeometryObject.BoundingBox => XbimRect3D.Empty;

        object IXbimGeometryObject.Tag { get; set; }

        bool IXbimCurve.Is3D => Is3d;

        bool IXbimCurve.IsClosed => XbimGeometryNativeApi.xbim_curve_is_closed(Handle, 1e-7) != 0;

        string IXbimCurve.ToBRep => string.Empty;

        XbimPoint3D IXbimCurve.Start
        {
            get
            {
                var pt = GetPoint(FirstParameter);
                return new XbimPoint3D(pt.X, pt.Y, pt.Z);
            }
        }

        XbimPoint3D IXbimCurve.End
        {
            get
            {
                var pt = GetPoint(LastParameter);
                return new XbimPoint3D(pt.X, pt.Y, pt.Z);
            }
        }

        double IXbimCurve.GetParameter(XbimPoint3D point, double tolerance)
        {
            throw new NotSupportedException("Curve parameter projection not yet supported.");
        }

        XbimPoint3D IXbimCurve.GetPoint(double parameter)
        {
            var pt = GetPoint(parameter);
            return new XbimPoint3D(pt.X, pt.Y, pt.Z);
        }

        IEnumerable<XbimPoint3D> IXbimCurve.Intersections(IXbimCurve intersector, double tolerance, ILogger logger)
        {
            return Enumerable.Empty<XbimPoint3D>();
        }

        IXbimGeometryObject IXbimGeometryObject.Transform(XbimMatrix3D matrix3D) => this;

        IXbimGeometryObject IXbimGeometryObject.TransformShallow(XbimMatrix3D matrix3D) => this;

        #endregion
    }
}
