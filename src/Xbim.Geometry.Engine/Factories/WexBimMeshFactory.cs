using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Creates WexBim mesh byte buffers from 3D shapes, including triangulation,
    /// vertex deduplication, and packed normal encoding.
    /// </summary>
    internal class WexBimMeshFactory : IXWexBimMeshFactory
    {
        private readonly ModelGeometryService _modelService;
        private readonly ILogger _logger;

        public WexBimMeshFactory(ModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        public byte[] CreateWexBimMesh(IXShape shape, out IXAxisAlignedBoundingBox bounds)
        {
            var mf = _modelService.MeshFactors;
            return CreateWexBimMesh(shape, mf.Tolerance, mf.LinearDefection, mf.AngularDeflection, 1.0, out bounds);
        }

        public byte[] CreateWexBimMesh(IXShape shape, IXMeshFactors meshFactors, double scale, out IXAxisAlignedBoundingBox bounds)
        {
            return CreateWexBimMesh(shape, meshFactors.Tolerance, meshFactors.LinearDefection, meshFactors.AngularDeflection, scale, out bounds);
        }

        public byte[] CreateWexBimMesh(IXShape shape, out IXAxisAlignedBoundingBox bounds, out bool hasCurves)
        {
            var mf = _modelService.MeshFactors;
            return CreateWexBimMesh(shape, mf.Tolerance, mf.LinearDefection, mf.AngularDeflection, 1.0, out bounds, out hasCurves);
        }

        public byte[] CreateWexBimMesh(IXShape shape, IXMeshFactors meshFactors, double scale, out IXAxisAlignedBoundingBox bounds, out bool hasCurves)
        {
            return CreateWexBimMesh(shape, meshFactors.Tolerance, meshFactors.LinearDefection, meshFactors.AngularDeflection, scale, out bounds, out hasCurves);
        }

        public byte[] CreateWexBimMesh(IXShape shape, double tolerance, double linearDeflection, double angularDeflection, double scale, out IXAxisAlignedBoundingBox bounds)
        {
            return CreateWexBimMesh(shape, tolerance, linearDeflection, angularDeflection, scale, out bounds, out _);
        }

        public byte[] CreateWexBimMesh(IXShape shape, double tolerance, double linearDeflection, double angularDeflection, double scale, out IXAxisAlignedBoundingBox bounds, out bool hasCurves)
        {
            ArgumentNullException.ThrowIfNull(shape);
            var Shape = shape as XbimShape
                ?? throw new ArgumentException("Shape must be an XbimShape instance.", nameof(shape));

            int result = XbimGeometryNativeApi.xbim_mesh_create_wexbim(
                _modelService.ContextHandle,
                Shape.Handle,
                tolerance,
                linearDeflection,
                angularDeflection,
                scale,
                0, // checkEdges = false
                out IntPtr bufferPtr,
                out int bufferSize,
                out int nativeHasCurves,
                out double minX, out double minY, out double minZ,
                out double maxX, out double maxY, out double maxZ);

            if (result != 0 || bufferPtr == IntPtr.Zero)
            {
                string error = XbimGeometryNativeApi.GetLastError();
                _logger.LogWarning("WexBim mesh creation failed: {Error}", error);
                bounds = XAxisAlignedBoundingBox.Void;
                hasCurves = false;
                return Array.Empty<byte>();
            }

            try
            {
                // Copy native buffer to managed byte array
                var meshBytes = new byte[bufferSize];
                Marshal.Copy(bufferPtr, meshBytes, 0, bufferSize);
                hasCurves = nativeHasCurves != 0;
                bounds = new XAxisAlignedBoundingBox(minX, minY, minZ, maxX, maxY, maxZ);

                return meshBytes;
            }
            finally
            {
                XbimGeometryNativeApi.xbim_buffer_free(bufferPtr);
            }
        }
    }
}
