using System;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Engine.Primitives;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Engine.Shapes;

namespace Xbim.Geometry.Engine.Factories
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

            var meshParams = new XbimGeometryNativeApi.XbimMeshParams
            {
                Tolerance = tolerance,
                LinearDeflection = linearDeflection,
                AngularDeflection = angularDeflection,
                Scale = scale,
                CheckEdges = 0
            };

            int result = XbimGeometryNativeApi.xbim_mesh_prepare(
                _modelService.ContextHandle,
                Shape.Handle,
                in meshParams,
                out var meshHandle,
                out int bufferSize,
                out int nativeHasCurves,
                out var bbox);

            if (result != 0 || meshHandle.IsInvalid)
            {
                string error = XbimGeometryNativeApi.GetLastError();
                _logger.LogWarning("WexBim mesh creation failed: {Error}", error);
                meshHandle?.Dispose();
                bounds = XAxisAlignedBoundingBox.Void;
                hasCurves = false;
                return Array.Empty<byte>();
            }

            try
            {
                var meshBytes = new byte[bufferSize];
                unsafe
                {
                    fixed (byte* ptr = meshBytes)
                    {
                        int writeResult = XbimGeometryNativeApi.xbim_mesh_write(
                            meshHandle, ptr, bufferSize);
                        if (writeResult != 0)
                        {
                            string error = XbimGeometryNativeApi.GetLastError();
                            throw new InvalidOperationException(
                                $"WexBim mesh write failed: {error}");
                        }
                    }
                }
                hasCurves = nativeHasCurves != 0;
                bounds = new XAxisAlignedBoundingBox(
                    bbox.MinX, bbox.MinY, bbox.MinZ,
                    bbox.MaxX, bbox.MaxY, bbox.MaxZ);
                return meshBytes;
            }
            finally
            {
                meshHandle.Dispose();
            }
        }
    }
}
