using System;
using System.Globalization;
using Xbim.Geometry.Abstractions;

namespace Xbim.Geometry.Engine.Primitives
{
    internal class XAxisAlignedBoundingBox : IXAxisAlignedBoundingBox
    {
        private readonly double _minX, _minY, _minZ;
        private readonly double _maxX, _maxY, _maxZ;

        internal XAxisAlignedBoundingBox(
            double minX, double minY, double minZ,
            double maxX, double maxY, double maxZ)
        {
            _minX = minX; _minY = minY; _minZ = minZ;
            _maxX = maxX; _maxY = maxY; _maxZ = maxZ;
        }

        internal static XAxisAlignedBoundingBox Void =>
            new XAxisAlignedBoundingBox(
                double.MaxValue, double.MaxValue, double.MaxValue,
                double.MinValue, double.MinValue, double.MinValue);

        public IXPoint CornerMin => new XPoint(_minX, _minY, _minZ);
        public IXPoint CornerMax => new XPoint(_maxX, _maxY, _maxZ);

        public double LenX => _maxX - _minX;
        public double LenY => _maxY - _minY;
        public double LenZ => _maxZ - _minZ;

        public double Gap => Math.Max(Math.Max(LenX, LenY), LenZ) * 1e-6;

        public bool IsVoid => _minX > _maxX || _minY > _maxY || _minZ > _maxZ;

        public IXPoint Centroid => new XPoint(
            (_minX + _maxX) / 2.0,
            (_minY + _maxY) / 2.0,
            (_minZ + _maxZ) / 2.0);

        public string Json => string.Format(CultureInfo.InvariantCulture,
            "{{\"min\":[{0},{1},{2}],\"max\":[{3},{4},{5}]}}",
            _minX, _minY, _minZ, _maxX, _maxY, _maxZ);

        public IXAxisAlignedBoundingBox Union(IXAxisAlignedBoundingBox other)
        {
            if (other == null || other.IsVoid) return this;
            if (IsVoid) return other;

            return new XAxisAlignedBoundingBox(
                Math.Min(_minX, other.CornerMin.X),
                Math.Min(_minY, other.CornerMin.Y),
                Math.Min(_minZ, other.CornerMin.Z),
                Math.Max(_maxX, other.CornerMax.X),
                Math.Max(_maxY, other.CornerMax.Y),
                Math.Max(_maxZ, other.CornerMax.Z));
        }

        public IXAxisAlignedBoundingBox Transformed(IXLocation location)
        {
            if (location == null || location.IsIdentity) return this;
            return Transformed((IXMatrix)location);
        }

        public IXAxisAlignedBoundingBox Transformed(IXMatrix m)
        {
            if (m == null || m.IsIdentity) return this;
            if (IsVoid) return this;

            // Transform all 8 corners and recompute AABB
            Span<double> xs = stackalloc double[8];
            Span<double> ys = stackalloc double[8];
            Span<double> zs = stackalloc double[8];

            int i = 0;
            for (int cx = 0; cx < 2; cx++)
            for (int cy = 0; cy < 2; cy++)
            for (int cz = 0; cz < 2; cz++)
            {
                double px = cx == 0 ? _minX : _maxX;
                double py = cy == 0 ? _minY : _maxY;
                double pz = cz == 0 ? _minZ : _maxZ;

                xs[i] = m.ScaleX * (m.M11 * px + m.M12 * py + m.M13 * pz) + m.OffsetX;
                ys[i] = m.ScaleY * (m.M21 * px + m.M22 * py + m.M23 * pz) + m.OffsetY;
                zs[i] = m.ScaleZ * (m.M31 * px + m.M32 * py + m.M33 * pz) + m.OffsetZ;
                i++;
            }

            double newMinX = double.MaxValue, newMinY = double.MaxValue, newMinZ = double.MaxValue;
            double newMaxX = double.MinValue, newMaxY = double.MinValue, newMaxZ = double.MinValue;

            for (int j = 0; j < 8; j++)
            {
                if (xs[j] < newMinX) newMinX = xs[j];
                if (xs[j] > newMaxX) newMaxX = xs[j];
                if (ys[j] < newMinY) newMinY = ys[j];
                if (ys[j] > newMaxY) newMaxY = ys[j];
                if (zs[j] < newMinZ) newMinZ = zs[j];
                if (zs[j] > newMaxZ) newMaxZ = zs[j];
            }

            return new XAxisAlignedBoundingBox(newMinX, newMinY, newMinZ, newMaxX, newMaxY, newMaxZ);
        }

        public IXAxisAlignedBoundingBox Translated(double xTranslation, double yTranslation, double zTranslation)
        {
            if (IsVoid) return this;
            return new XAxisAlignedBoundingBox(
                _minX + xTranslation, _minY + yTranslation, _minZ + zTranslation,
                _maxX + xTranslation, _maxY + yTranslation, _maxZ + zTranslation);
        }

        public IXAxisAlignedBoundingBox Translated(IXPoint translation)
        {
            if (translation == null) return this;
            return Translated(translation.X, translation.Y, translation.Z);
        }
    }
}
