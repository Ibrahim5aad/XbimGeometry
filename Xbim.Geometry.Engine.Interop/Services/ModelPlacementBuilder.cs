using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Factories;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Services
{
    /// <summary>
    /// Analyses an IFC model's placement tree to identify the world coordinate system
    /// and root placement, then builds composed location transforms for object placements.
    /// </summary>
    internal class ModelPlacementBuilder : IXModelPlacementBuilder
    {
        private readonly ModelGeometryService _modelService;
        private readonly GeometryFactory _geometryFactory;
        private readonly ILogger _logger;

        private int _rootId = -1;
        private IXPoint _worldCoordinateSystem;
        private IXLocation _rootPlacement;

        public ModelPlacementBuilder(ModelGeometryService modelService, ILogger logger)
        {
            _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _geometryFactory = (GeometryFactory)modelService.GeometryFactory;

            _worldCoordinateSystem = new XPoint(0, 0, 0);
            _rootPlacement = new XLocation();

            AnalysePlacementTree();
        }

        public IXPoint WorldCoordinateSystem => _worldCoordinateSystem;

        public IXLocation RootPlacement => _rootPlacement;

        public IXLocation BuildLocation(IIfcObjectPlacement placement, bool adjustWcs)
        {
            if (placement is IIfcLocalPlacement localPlacement)
                return BuildLocalPlacement(localPlacement, adjustWcs);

            _logger.LogWarning(
                "Placement type {Type} is not supported, returning identity.",
                placement.GetType().Name);
            return new XLocation();
        }

        private void AnalysePlacementTree()
        {
            var model = _modelService.Model;
            var localPlacements = model.Instances.OfType<IIfcLocalPlacement>().ToList();

            // Find root placements (those with no parent)
            var roots = localPlacements
                .Where(p => p.PlacementRelTo == null)
                .ToList();

            if (roots.Count == 1)
            {
                var root = roots[0];
                _rootId = root.EntityLabel;

                // Build the root transform
                if (root.RelativePlacement is IIfcAxis2Placement3D axis3D)
                {
                    var rootLocation = _geometryFactory.BuildLocationFromAxis3D(axis3D);

                    // Extract the WCS origin (translation component of root placement)
                    _worldCoordinateSystem = rootLocation.Translation;

                    // Create root placement with translation stripped
                    _rootPlacement = rootLocation.Translated(0, 0, 0);
                    rootLocation.Dispose();
                }
            }
            else if (roots.Count > 1)
            {
                _logger.LogDebug(
                    "Multiple root placements found ({Count}), WCS adjustment disabled.",
                    roots.Count);
            }
        }

        private IXLocation BuildLocalPlacement(IIfcLocalPlacement placement, bool adjustWcs)
        {
            XLocation? accumulated = null;
            var current = placement;

            while (current != null)
            {
                XLocation stepLocation;

                if (adjustWcs && current.EntityLabel == _rootId)
                {
                    // At root: use identity (the WCS offset is stripped)
                    stepLocation = new XLocation();
                }
                else if (current.RelativePlacement is IIfcAxis2Placement3D axis3D)
                {
                    stepLocation = _geometryFactory.BuildLocationFromAxis3D(axis3D);
                }
                else
                {
                    _logger.LogWarning(
                        "Non-3D relative placement in #{Label}, using identity.",
                        current.EntityLabel);
                    stepLocation = new XLocation();
                }

                if (accumulated == null)
                {
                    accumulated = stepLocation;
                }
                else
                {
                    // PreMultiply: accumulated = stepLocation * accumulated
                    var composed = (XLocation)stepLocation.Multiplied(accumulated);
                    accumulated.Dispose();
                    stepLocation.Dispose();
                    accumulated = composed;
                }

                // Navigate up the placement hierarchy
                current = current.PlacementRelTo as IIfcLocalPlacement;
            }

            return accumulated ?? new XLocation();
        }
    }
}
