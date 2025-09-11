using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;

namespace Xbim.Geometry.Engine.Interop.Shapes.V5
{
    /// <summary>
    /// Adapts a V6 <see cref="Curve"/> to the legacy <see cref="IXbimCurve"/> interface.
    /// </summary>
    internal class V5Curve : IXbimCurve
    {
        private readonly Curve _curve;

        internal V5Curve(Curve curve)
        {
            _curve = curve ?? throw new ArgumentNullException(nameof(curve));
        }

        public XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimCurveType;

        public bool IsValid => true;

        public bool IsSet => false;

        public XbimRect3D BoundingBox => XbimRect3D.Empty;

        public object Tag { get; set; }

        public double Length => _curve.Length;

        public XbimPoint3D Start
        {
            get
            {
                var pt = _curve.GetPoint(_curve.FirstParameter);
                return new XbimPoint3D(pt.X, pt.Y, pt.Z);
            }
        }

        public XbimPoint3D End
        {
            get
            {
                var pt = _curve.GetPoint(_curve.LastParameter);
                return new XbimPoint3D(pt.X, pt.Y, pt.Z);
            }
        }

        public bool IsClosed => XbimGeometryNativeApi.xbim_curve_is_closed(
            _curve.Handle, 1e-7) != 0;

        public bool Is3D => _curve.Is3d;

        public string ToBRep => string.Empty;

        public double GetParameter(XbimPoint3D point, double tolerance)
        {
            throw new NotSupportedException("Curve parameter projection not yet supported.");
        }

        public XbimPoint3D GetPoint(double parameter)
        {
            var pt = _curve.GetPoint(parameter);
            return new XbimPoint3D(pt.X, pt.Y, pt.Z);
        }

        public IEnumerable<XbimPoint3D> Intersections(IXbimCurve intersector, double tolerance, ILogger logger = null)
        {
            return Enumerable.Empty<XbimPoint3D>();
        }

        public IXbimGeometryObject Transform(XbimMatrix3D matrix3D) => this;

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D) => this;

        public void Dispose() { }
    }
}
