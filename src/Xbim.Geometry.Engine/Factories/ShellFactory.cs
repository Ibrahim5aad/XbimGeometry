using System;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Services;

namespace Xbim.Geometry.Engine.Factories
{
    /// <summary>
    /// Builds shell shapes from collections of faces. Shells represent
    /// connected sets of faces forming open or closed surfaces.
    /// </summary>
    internal class ShellFactory : IXShellFactory
    {
        private readonly ModelGeometryService _modelService;
        private readonly ILogger _logger;

        public ShellFactory(ModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IXModelGeometryService ModelGeometryService => _modelService;
        public IXLoggingService LoggingService => _modelService.LoggingService;

        // IXShellFactory currently has no Build methods defined in the interface.
        // Shell construction is available via the native API (xbim_shell_build_from_faces,
        // xbim_shell_sew, xbim_shell_make_solid) and will be exposed when the
        // interface is extended or when higher-level factories (SolidFactory) need
        // shell construction for faceted BRep and shell-based surface models.
    }
}
