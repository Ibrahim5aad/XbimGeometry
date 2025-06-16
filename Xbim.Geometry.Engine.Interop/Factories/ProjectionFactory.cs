using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Computes 2D footprints and cross-sections from 3D shapes.
    /// Currently a stub — full implementation requires native OCCT HLR and
    /// section algorithms to be exported.
    /// </summary>
    internal class ProjectionFactory : IXProjectionFactory
    {
        private readonly ModelGeometryService _modelService;
        private readonly ILogger _logger;

        public ProjectionFactory(ModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        public IXFootprint CreateFootprint(IXShape shape, double linearDeflection,
            double angularDeflection = 0.52359877559829887307710723054658,
            bool createExactFootprint = true)
        {
            throw new NotImplementedException(
                "Footprint generation requires native HLR (Hidden Line Removal) algorithm export.");
        }

        public IXFootprint CreateFootprint(IXShape shape, bool createExactFootprint = true)
        {
            double linearDeflection = _modelService.OneMeter * 0.025; // 25mm default
            return CreateFootprint(shape, linearDeflection, 0.523598775, createExactFootprint);
        }

        public IXCompound GetOutline(IXShape shape)
        {
            throw new NotImplementedException(
                "Shape outlining requires native HLR algorithm export.");
        }

        public IEnumerable<IXFace> CreateSection(IXShape shape, IXPlane cutPlane)
        {
            throw new NotImplementedException(
                "Section cutting requires native BRepAlgoAPI_Section algorithm export.");
        }
    }
}
