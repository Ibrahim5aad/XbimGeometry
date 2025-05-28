using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Builds compound shapes from collections of sub-shapes.
    /// </summary>
    internal class NativeCompoundFactory : IXCompoundFactory
    {
        private readonly NativeModelGeometryService _modelService;
        private readonly ILogger _logger;

        public NativeCompoundFactory(NativeModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        public IXCompound CreateEmpty()
        {
            var handles = Array.Empty<IntPtr>();
            int result = XbimGeometryNativeApi.xbim_compound_make(
                ContextHandle, handles, 0, out var outHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to create empty compound: {XbimGeometryNativeApi.GetLastError()}");

            return new NativeCompound(outHandle);
        }

        public IXCompound CreateFrom(IEnumerable<IXShape> shapes)
        {
            if (shapes == null) throw new ArgumentNullException(nameof(shapes));

            var shapeList = shapes.ToList();
            if (shapeList.Count == 0)
                return CreateEmpty();

            // Extract native handles - keep references alive to prevent GC during P/Invoke
            var nativeShapes = new NativeShape[shapeList.Count];
            var handlePtrs = new IntPtr[shapeList.Count];

            for (int i = 0; i < shapeList.Count; i++)
            {
                if (shapeList[i] is NativeShape ns)
                {
                    nativeShapes[i] = ns;
                    handlePtrs[i] = ns.Handle.DangerousGetHandle();
                }
                else
                {
                    throw new ArgumentException(
                        $"Shape at index {i} is not a NativeShape instance.", nameof(shapes));
                }
            }

            int result = XbimGeometryNativeApi.xbim_compound_make(
                ContextHandle, handlePtrs, handlePtrs.Length, out var outHandle);

            // Keep nativeShapes alive across the P/Invoke call
            GC.KeepAlive(nativeShapes);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to create compound from {shapeList.Count} shapes: {XbimGeometryNativeApi.GetLastError()}");

            return new NativeCompound(outHandle);
        }
    }
}
