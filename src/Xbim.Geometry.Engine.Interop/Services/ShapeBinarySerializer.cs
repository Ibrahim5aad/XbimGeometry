using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Shapes;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Interop.Services
{
    /// <summary>
    /// Serializes and deserializes shapes to/from compact binary format
    /// using OCCT BinTools, with optional embedded triangulation and normals.
    /// </summary>
    internal class ShapeBinarySerializer : IXShapeBinarySerializer
    {
        private readonly ILogger _logger;

        public ShapeBinarySerializer(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public byte[] ToArray(IXShape shape, bool withTriangles = false, bool withNormals = false)
        {
            ArgumentNullException.ThrowIfNull(shape);
            
            var Shape = shape as XbimShape
                ?? throw new ArgumentException("Shape must be a XbimShape instance.", nameof(shape));

            int result = XbimGeometryNativeApi.xbim_shape_to_binary(
                Shape.Handle,
                withTriangles ? 1 : 0,
                withNormals ? 1 : 0,
                out IntPtr bufferPtr,
                out int size);

            if (result != 0 || bufferPtr == IntPtr.Zero)
            {
                string error = XbimGeometryNativeApi.GetLastError();
                throw new XbimGeometryServiceException($"Failed to serialize shape to binary: {error}");
            }

            try
            {
                var bytes = new byte[size];
                Marshal.Copy(bufferPtr, bytes, 0, size);
                return bytes;
            }
            finally
            {
                XbimGeometryNativeApi.xbim_buffer_free(bufferPtr);
            }
        }

        public IXShape FromArray(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            
            if (bytes.Length == 0) throw new ArgumentException("Cannot deserialize from empty byte array.", nameof(bytes));

            // Pin the managed array and pass to native
            var pinnedArray = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                IntPtr bufferPtr = pinnedArray.AddrOfPinnedObject();
                var handle = XbimGeometryNativeApi.xbim_shape_from_binary(bufferPtr, bytes.Length);

                if (handle == null || handle.IsInvalid)
                {
                    string error = XbimGeometryNativeApi.GetLastError();
                    throw new XbimGeometryServiceException($"Failed to deserialize shape from binary: {error}");
                }

                return NativeShapeWrapper.WrapShape(handle);
            }
            finally
            {
                pinnedArray.Free();
            }
        }

        /// <summary>
        /// Converts a shape to its OCCT BRep ASCII string representation.
        /// </summary>
        internal static string ToBrep(XbimShape shape)
        {
            int result = XbimGeometryNativeApi.xbim_shape_to_brep_string(
                shape.Handle,
                out IntPtr strPtr,
                out int strLen);

            if (result != 0 || strPtr == IntPtr.Zero)
            {
                string error = XbimGeometryNativeApi.GetLastError();
                throw new XbimGeometryServiceException($"Failed to serialize shape to BRep: {error}");
            }

            try
            {
                return Marshal.PtrToStringAnsi(strPtr, strLen)!;
            }
            finally
            {
                XbimGeometryNativeApi.xbim_string_free(strPtr);
            }
        }

        /// <summary>
        /// Creates a shape from an OCCT BRep ASCII string representation.
        /// </summary>
        internal static IXShape FromBrep(string brepString)
        {
            if (string.IsNullOrEmpty(brepString))
                throw new ArgumentException("BRep string cannot be null or empty.", nameof(brepString));

            var handle = XbimGeometryNativeApi.xbim_shape_from_brep_string(brepString, brepString.Length);

            if (handle == null || handle.IsInvalid)
            {
                string error = XbimGeometryNativeApi.GetLastError();
                throw new XbimGeometryServiceException($"Failed to deserialize shape from BRep: {error}");
            }

            return NativeShapeWrapper.WrapShape(handle);
        }
    }
}
