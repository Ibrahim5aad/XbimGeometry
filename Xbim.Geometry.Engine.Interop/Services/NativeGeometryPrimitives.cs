using System;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Primitives;

namespace Xbim.Geometry.Engine.Interop.Services
{
    /// <summary>
    /// Factory for building geometry primitive objects (points, directions, locations,
    /// matrices, bounding boxes, and mesh factors).
    /// </summary>
    internal class NativeGeometryPrimitives : IXGeometryPrimitives
    {
        public IXPoint BuildPoint3d(double x, double y, double z)
            => new XPoint(x, y, z);

        public IXPoint BuildPoint2d(double x, double y)
            => new XPoint(x, y);

        public IXDirection BuildDirection3d(double x, double y, double z)
            => new XDirection(x, y, z);

        public IXDirection BuildDirection2d(double x, double y)
            => new XDirection(x, y);

        public IXLocation BuildLocation(double tx, double ty, double tz, double sc,
            double qw, double qx, double qy, double qz)
        {
            // Convert quaternion to rotation matrix
            double norm = Math.Sqrt(qw * qw + qx * qx + qy * qy + qz * qz);
            if (norm < 1e-15)
                return new XLocation(); // identity

            double w = qw / norm, x = qx / norm, y = qy / norm, z = qz / norm;

            double m11 = 1 - 2 * (y * y + z * z);
            double m12 = 2 * (x * y - z * w);
            double m13 = 2 * (x * z + y * w);
            double m21 = 2 * (x * y + z * w);
            double m22 = 1 - 2 * (x * x + z * z);
            double m23 = 2 * (y * z - x * w);
            double m31 = 2 * (x * z - y * w);
            double m32 = 2 * (y * z + x * w);
            double m33 = 1 - 2 * (x * x + y * y);

            int result = Internal.XbimGeometryNativeApi.xbim_location_create_from_axis2(
                tx, ty, tz, m31, m32, m33, m11, m12, m13,
                out var handle);

            if (result != 0)
                return new XLocation(); // fallback to identity

            return new XLocation(handle, m11, m12, m13, m21, m22, m23, m31, m32, m33,
                tx, ty, tz, sc);
        }

        public IXMatrix BuildMatrix(double[] values)
        {
            if (values == null || values.Length < 16)
                return new XMatrix();

            return new XMatrix(
                values[0], values[1], values[2],
                values[4], values[5], values[6],
                values[8], values[9], values[10],
                values[12], values[13], values[14],
                values[3], values[7], values[11]);
        }

        public IXAxisAlignedBoundingBox Moved(IXAxisAlignedBoundingBox box, IXLocation newLocation)
        {
            if (box == null) return XAxisAlignedBoundingBox.Void;
            return box.Transformed(newLocation);
        }

        public IXAxisAlignedBoundingBox BuildBoundingBox()
            => XAxisAlignedBoundingBox.Void;

        public IXAxisAlignedBoundingBox BuildBoundingBox(double x, double y, double z,
            double sizeX, double sizeY, double sizeZ)
        {
            return new XAxisAlignedBoundingBox(x, y, z, x + sizeX, y + sizeY, z + sizeZ);
        }

        public IXMeshFactors GetMeshFactors(MeshGranularity granularity, double oneMeter, double precision)
        {
            var factors = new MeshFactors(oneMeter, precision);
            factors.SetGranularity(granularity);
            return factors;
        }
    }
}
