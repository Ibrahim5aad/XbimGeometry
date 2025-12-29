using System;
using System.IO;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;

namespace Xbim.Geometry.Engine.Interop.Primitives
{
    /// <summary>
    /// Wraps a native OCCT location handle and exposes the transform as an IXLocation.
    /// The matrix components are read from the native handle at construction time;
    /// all derived operations (compose, invert, translate, scale) delegate to native
    /// OCCT so the handle and managed fields are always consistent.
    /// </summary>
    internal class XLocation : NativeOwner<NativeLocationHandle>, IXLocation
    {
        // Rotation/scale matrix (3x3) - IXMatrix convention (rows = local axis directions).
        private readonly double _m11, _m12, _m13;
        private readonly double _m21, _m22, _m23;
        private readonly double _m31, _m32, _m33;
        private readonly double _offsetX, _offsetY, _offsetZ;
        private readonly double _scale;

        /// <summary>
        /// Creates an XLocation from a native location handle, reading the transform
        /// components from native via P/Invoke. Takes ownership of the handle.
        /// </summary>
        internal XLocation(NativeLocationHandle handle) : base(handle)
        {
            int result = XbimGeometryNativeApi.xbim_location_get_transform(handle,
                out _m11, out _m12, out _m13,
                out _m21, out _m22, out _m23,
                out _m31, out _m32, out _m33,
                out _offsetX, out _offsetY, out _offsetZ,
                out _scale);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to read location transform: {XbimGeometryNativeApi.GetLastError()}");
        }

        /// <summary>
        /// Creates an XLocation with explicit matrix components.
        /// Use this when the matrix is already known from the creation inputs
        /// (e.g. BuildLocationFromAxis3D) to avoid an extra P/Invoke round-trip.
        /// </summary>
        internal XLocation(
            NativeLocationHandle handle,
            double m11, double m12, double m13,
            double m21, double m22, double m23,
            double m31, double m32, double m33,
            double offsetX, double offsetY, double offsetZ,
            double scale) : base(handle)
        {
            _m11 = m11; _m12 = m12; _m13 = m13;
            _m21 = m21; _m22 = m22; _m23 = m23;
            _m31 = m31; _m32 = m32; _m33 = m33;
            _offsetX = offsetX; _offsetY = offsetY; _offsetZ = offsetZ;
            _scale = scale;
        }

        /// <summary>
        /// Creates an identity XLocation.
        /// </summary>
        internal XLocation() : base(CreateIdentityHandle())
        {
            _m11 = 1; _m12 = 0; _m13 = 0;
            _m21 = 0; _m22 = 1; _m23 = 0;
            _m31 = 0; _m32 = 0; _m33 = 1;
            _offsetX = 0; _offsetY = 0; _offsetZ = 0;
            _scale = 1.0;
        }

        private static NativeLocationHandle CreateIdentityHandle()
        {
            int result = XbimGeometryNativeApi.xbim_location_create_identity(out var handle);
            if (result != 0)
                throw new InvalidOperationException("Failed to create identity location.");
            return handle;
        }

        #region IXMatrix

        public bool IsIdentity =>
            Math.Abs(_m11 - 1) < 1e-12 && Math.Abs(_m12) < 1e-12 && Math.Abs(_m13) < 1e-12 &&
            Math.Abs(_m21) < 1e-12 && Math.Abs(_m22 - 1) < 1e-12 && Math.Abs(_m23) < 1e-12 &&
            Math.Abs(_m31) < 1e-12 && Math.Abs(_m32) < 1e-12 && Math.Abs(_m33 - 1) < 1e-12 &&
            Math.Abs(_offsetX) < 1e-12 && Math.Abs(_offsetY) < 1e-12 && Math.Abs(_offsetZ) < 1e-12 &&
            Math.Abs(_scale - 1) < 1e-12;

        public double M11 => _m11;
        public double M12 => _m12;
        public double M13 => _m13;
        public double M21 => _m21;
        public double M22 => _m22;
        public double M23 => _m23;
        public double M31 => _m31;
        public double M32 => _m32;
        public double M33 => _m33;
        public double OffsetX => _offsetX;
        public double OffsetY => _offsetY;
        public double OffsetZ => _offsetZ;
        public double ScaleX => _scale;
        public double ScaleY => _scale;
        public double ScaleZ => _scale;
        public double M44 => 1.0;

        public double[] Values => new double[]
        {
            M11, M12, M13, ScaleX,
            M21, M22, M23, ScaleY,
            M31, M32, M33, ScaleZ,
            OffsetX, OffsetY, OffsetZ, M44
        };

        public IXMatrix Multiply(IXMatrix matrix)
        {
            // 4x4 matrix multiplication (row-major layout matching legacy XLocation.h Values order)
            return new XMatrix(
                _m11 * matrix.M11 + _m12 * matrix.M21 + _m13 * matrix.M31,
                _m11 * matrix.M12 + _m12 * matrix.M22 + _m13 * matrix.M32,
                _m11 * matrix.M13 + _m12 * matrix.M23 + _m13 * matrix.M33,
                _m21 * matrix.M11 + _m22 * matrix.M21 + _m23 * matrix.M31,
                _m21 * matrix.M12 + _m22 * matrix.M22 + _m23 * matrix.M32,
                _m21 * matrix.M13 + _m22 * matrix.M23 + _m23 * matrix.M33,
                _m31 * matrix.M11 + _m32 * matrix.M21 + _m33 * matrix.M31,
                _m31 * matrix.M12 + _m32 * matrix.M22 + _m33 * matrix.M32,
                _m31 * matrix.M13 + _m32 * matrix.M23 + _m33 * matrix.M33,
                _offsetX * matrix.M11 + _offsetY * matrix.M21 + _offsetZ * matrix.M31 + matrix.OffsetX,
                _offsetX * matrix.M12 + _offsetY * matrix.M22 + _offsetZ * matrix.M32 + matrix.OffsetY,
                _offsetX * matrix.M13 + _offsetY * matrix.M23 + _offsetZ * matrix.M33 + matrix.OffsetZ,
                _scale * matrix.ScaleX, _scale * matrix.ScaleY, _scale * matrix.ScaleZ);
        }

        public byte[] ToByteArray()
        {
            using var ms = new MemoryStream(16 * sizeof(double));
            using var bw = new BinaryWriter(ms);
            foreach (var val in Values)
                bw.Write(val);
            return ms.ToArray();
        }

        #endregion

        #region IXLocation

        public double Scale => _scale;

        public IXPoint Translation => new XPoint(_offsetX, _offsetY, _offsetZ);

        public IXQuaternion Rotation
        {
            get
            {
                // Extract quaternion from rotation matrix
                // Using the standard rotation matrix -> quaternion conversion
                double trace = _m11 + _m22 + _m33;
                double w, x, y, z;

                if (trace > 0)
                {
                    double s = 0.5 / Math.Sqrt(trace + 1.0);
                    w = 0.25 / s;
                    x = (_m32 - _m23) * s;
                    y = (_m13 - _m31) * s;
                    z = (_m21 - _m12) * s;
                }
                else if (_m11 > _m22 && _m11 > _m33)
                {
                    double s = 2.0 * Math.Sqrt(1.0 + _m11 - _m22 - _m33);
                    w = (_m32 - _m23) / s;
                    x = 0.25 * s;
                    y = (_m12 + _m21) / s;
                    z = (_m13 + _m31) / s;
                }
                else if (_m22 > _m33)
                {
                    double s = 2.0 * Math.Sqrt(1.0 + _m22 - _m11 - _m33);
                    w = (_m13 - _m31) / s;
                    x = (_m12 + _m21) / s;
                    y = 0.25 * s;
                    z = (_m23 + _m32) / s;
                }
                else
                {
                    double s = 2.0 * Math.Sqrt(1.0 + _m33 - _m11 - _m22);
                    w = (_m21 - _m12) / s;
                    x = (_m13 + _m31) / s;
                    y = (_m23 + _m32) / s;
                    z = 0.25 * s;
                }

                return new XQuaternion(w, x, y, z);
            }
        }

        public IXLocation Multiplied(IXLocation location)
        {
            if (location == null || location.IsIdentity)
                return this;

            var other = location as XLocation;
            if (other == null)
                throw new ArgumentException("Location must be an XLocation from the native interop layer.", nameof(location));

            // Native compose: result = other * this (OCCT column-vector convention)
            // Semantics: "apply this first, then other"
            int result = XbimGeometryNativeApi.xbim_location_compose(other.Handle, Handle, out var composed);
            if (result != 0)
                throw new InvalidOperationException($"Failed to compose locations: {XbimGeometryNativeApi.GetLastError()}");

            // Read the matrix from the composed native handle — single source of truth
            return new XLocation(composed);
        }

        public IXLocation Inverted()
        {
            int result = XbimGeometryNativeApi.xbim_location_invert(Handle, out var inverted);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to invert location: {XbimGeometryNativeApi.GetLastError()}");

            return new XLocation(inverted);
        }

        public IXLocation ScaledBy(double scaleFactor)
        {
            int result = XbimGeometryNativeApi.xbim_location_scaled(Handle, scaleFactor, out var scaled);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to scale location: {XbimGeometryNativeApi.GetLastError()}");

            return new XLocation(scaled);
        }

        public void SetTranslation(double x, double y, double z)
        {
            throw new NotSupportedException("XLocation is immutable. Use Translated() instead.");
        }

        public IXLocation Translated(double x, double y, double z)
        {
            int result = XbimGeometryNativeApi.xbim_location_translated(Handle, x, y, z, out var translated);
            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to translate location: {XbimGeometryNativeApi.GetLastError()}");

            return new XLocation(translated);
        }

        #endregion
    }
}
