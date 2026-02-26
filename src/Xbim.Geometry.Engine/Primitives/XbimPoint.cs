using System;
using Xbim.Common.Geometry;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    internal sealed class XbimPoint : IXbimPoint
    {
        private readonly double _tolerance;

        public XbimPoint(double x, double y, double z, double tolerance)
        {
            X = x;
            Y = y;
            Z = z;
            _tolerance = tolerance;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double Tolerance => _tolerance;
        public XbimPoint3D Point => new XbimPoint3D(X, Y, Z);

        // IXbimGeometryObject
        public XbimGeometryObjectType GeometryType => XbimGeometryObjectType.XbimPointType;
        public bool IsValid => true;
        public bool IsSet => false;
        public XbimRect3D BoundingBox => new XbimRect3D(X, Y, Z, 0, 0, 0);
        public object Tag { get; set; }

        public IXbimGeometryObject Transform(XbimMatrix3D matrix3D)
        {
            var pt = matrix3D.Transform(new XbimPoint3D(X, Y, Z));
            return new XbimPoint(pt.X, pt.Y, pt.Z, _tolerance);
        }

        public IXbimGeometryObject TransformShallow(XbimMatrix3D matrix3D) => Transform(matrix3D);

        public void Dispose() { }

        public bool Equals(IXbimPoint other)
        {
            if (other == null) return false;
            return Math.Abs(X - other.X) <= _tolerance
                && Math.Abs(Y - other.Y) <= _tolerance
                && Math.Abs(Z - other.Z) <= _tolerance;
        }
    }
}
