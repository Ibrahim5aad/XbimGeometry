using System;
using System.IO;
using Xbim.Geometry.Abstractions;

namespace Xbim.Geometry.Engine.Primitives
{
    /// <summary>
    /// Pure C# implementation of <see cref="IXMatrix"/> for representing 4x4 transforms.
    /// Used primarily for IIfcCartesianTransformationOperator results which may have
    /// non-uniform scale (not representable as a rigid gp_Trsf / NativeLocationHandle).
    /// </summary>
    internal class XMatrix : IXMatrix
    {
        public double M11 { get; }
        public double M12 { get; }
        public double M13 { get; }
        public double M21 { get; }
        public double M22 { get; }
        public double M23 { get; }
        public double M31 { get; }
        public double M32 { get; }
        public double M33 { get; }
        public double OffsetX { get; }
        public double OffsetY { get; }
        public double OffsetZ { get; }
        public double ScaleX { get; set; }
        public double ScaleY { get; set; }
        public double ScaleZ { get; set; }
        public double M44 => 1.0;

        public XMatrix()
        {
            M11 = 1; M12 = 0; M13 = 0;
            M21 = 0; M22 = 1; M23 = 0;
            M31 = 0; M32 = 0; M33 = 1;
            OffsetX = 0; OffsetY = 0; OffsetZ = 0;
            ScaleX = 1; ScaleY = 1; ScaleZ = 1;
        }

        public XMatrix(
            double m11, double m12, double m13,
            double m21, double m22, double m23,
            double m31, double m32, double m33,
            double offsetX, double offsetY, double offsetZ,
            double scaleX, double scaleY, double scaleZ)
        {
            M11 = m11; M12 = m12; M13 = m13;
            M21 = m21; M22 = m22; M23 = m23;
            M31 = m31; M32 = m32; M33 = m33;
            OffsetX = offsetX; OffsetY = offsetY; OffsetZ = offsetZ;
            ScaleX = scaleX; ScaleY = scaleY; ScaleZ = scaleZ;
        }

        public bool IsIdentity =>
            Math.Abs(M11 - 1) < 1e-12 && Math.Abs(M12) < 1e-12 && Math.Abs(M13) < 1e-12 &&
            Math.Abs(M21) < 1e-12 && Math.Abs(M22 - 1) < 1e-12 && Math.Abs(M23) < 1e-12 &&
            Math.Abs(M31) < 1e-12 && Math.Abs(M32) < 1e-12 && Math.Abs(M33 - 1) < 1e-12 &&
            Math.Abs(OffsetX) < 1e-12 && Math.Abs(OffsetY) < 1e-12 && Math.Abs(OffsetZ) < 1e-12 &&
            Math.Abs(ScaleX - 1) < 1e-12 && Math.Abs(ScaleY - 1) < 1e-12 && Math.Abs(ScaleZ - 1) < 1e-12;

        public double[] Values => new double[]
        {
            M11, M12, M13, ScaleX,
            M21, M22, M23, ScaleY,
            M31, M32, M33, ScaleZ,
            OffsetX, OffsetY, OffsetZ, M44
        };

        public IXMatrix Multiply(IXMatrix other)
        {
            return new XMatrix(
                M11 * other.M11 + M12 * other.M21 + M13 * other.M31,
                M11 * other.M12 + M12 * other.M22 + M13 * other.M32,
                M11 * other.M13 + M12 * other.M23 + M13 * other.M33,
                M21 * other.M11 + M22 * other.M21 + M23 * other.M31,
                M21 * other.M12 + M22 * other.M22 + M23 * other.M32,
                M21 * other.M13 + M22 * other.M23 + M23 * other.M33,
                M31 * other.M11 + M32 * other.M21 + M33 * other.M31,
                M31 * other.M12 + M32 * other.M22 + M33 * other.M32,
                M31 * other.M13 + M32 * other.M23 + M33 * other.M33,
                OffsetX * other.M11 + OffsetY * other.M21 + OffsetZ * other.M31 + other.OffsetX,
                OffsetX * other.M12 + OffsetY * other.M22 + OffsetZ * other.M32 + other.OffsetY,
                OffsetX * other.M13 + OffsetY * other.M23 + OffsetZ * other.M33 + other.OffsetZ,
                ScaleX * other.ScaleX, ScaleY * other.ScaleY, ScaleZ * other.ScaleZ);
        }

        public byte[] ToByteArray()
        {
            using var ms = new MemoryStream(16 * sizeof(double));
            using var bw = new BinaryWriter(ms);
            foreach (var val in Values)
                bw.Write(val);
            return ms.ToArray();
        }

        internal void SetScale(double sx, double sy, double sz)
        {
            ScaleX = sx;
            ScaleY = sy;
            ScaleZ = sz;
        }
    }
}
