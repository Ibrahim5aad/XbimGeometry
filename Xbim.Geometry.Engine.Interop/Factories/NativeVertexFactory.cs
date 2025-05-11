using System;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Handles;
using Xbim.Geometry.Engine.Interop.Internal;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Geometry.Engine.Interop.Shapes;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Builds vertex shapes from coordinate data. Provides vertex construction
    /// for topology building and point-based geometry operations.
    /// </summary>
    internal class NativeVertexFactory : IXVertexFactory
    {
        private readonly NativeModelGeometryService _modelService;
        private readonly ILogger _logger;

        public NativeVertexFactory(NativeModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        public IXVertex Build(double x, double y, double z = 0)
        {
            int result = NativeMethods.xbim_vertex_build(
                ContextHandle, x, y, z,
                _modelService.Precision,
                out var shapeHandle);

            if (result != 0)
                throw new InvalidOperationException(
                    $"Failed to build vertex at ({x}, {y}, {z}): {NativeMethods.GetLastError()}");

            return new NativeVertex(shapeHandle);
        }
    }
}
