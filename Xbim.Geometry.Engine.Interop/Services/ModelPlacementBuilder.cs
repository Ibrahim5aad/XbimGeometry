using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Factories;
using Xbim.Geometry.Engine.Interop.Primitives;
using Xbim.Ifc4.Interfaces;
using Xbim.Ifc4x3.GeometricConstraintResource;
using Xbim.Ifc4x3.GeometryResource;

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
            // Grid placement: delegate to geometry factory (no WCS adjustment)
            if (placement is IIfcGridPlacement)
                return _geometryFactory.BuildLocation(placement);

            // Local and linear placements: traverse the hierarchy with WCS adjustment
            if (placement is IIfcLocalPlacement || placement is IfcLinearPlacement)
                return BuildPlacement(placement, adjustWcs);

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

        /// <summary>
        /// Traverses a placement hierarchy that may contain both local and linear
        /// placements, composing the transforms and optionally adjusting for WCS.
        /// </summary>
        private IXLocation BuildPlacement(IIfcObjectPlacement placement, bool adjustWcs)
        {
            XLocation? accumulated = null;
            int rootId = adjustWcs ? _rootId : -1;

            var localPlacement = placement as IIfcLocalPlacement;
            var linearPlacement = placement as IfcLinearPlacement;

            while (localPlacement != null || linearPlacement != null)
            {
                XLocation stepLocation;

                if (localPlacement != null)
                {
                    if (localPlacement.EntityLabel == rootId)
                    {
                        stepLocation = new XLocation();
                    }
                    else if (localPlacement.RelativePlacement is IIfcAxis2Placement3D axis3D)
                    {
                        stepLocation = _geometryFactory.BuildLocationFromAxis3D(axis3D);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Non-3D relative placement in #{Label}, using identity.",
                            localPlacement.EntityLabel);
                        stepLocation = new XLocation();
                    }

                    accumulated = ComposeLocation(accumulated, stepLocation);

                    // Navigate up: PlacementRelTo can be local or linear
                    EvaluateNextPlacement(localPlacement.PlacementRelTo,
                        out localPlacement, out linearPlacement);
                }
                else if (linearPlacement != null)
                {
                    if (linearPlacement.RelativePlacement is IfcAxis2PlacementLinear axisLinear)
                    {
                        stepLocation = (XLocation)_geometryFactory.BuildLocation(axisLinear);

                        if (linearPlacement.EntityLabel == rootId)
                        {
                            // Keep orientation, strip translation
                            var adjusted = (XLocation)stepLocation.Translated(0, 0, 0);
                            stepLocation.Dispose();
                            stepLocation = adjusted;
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "IfcLinearPlacement #{Label} has no IfcAxis2PlacementLinear, using identity.",
                            linearPlacement.EntityLabel);
                        stepLocation = new XLocation();
                    }

                    accumulated = ComposeLocation(accumulated, stepLocation);

                    // Navigate up: PlacementRelTo can be local or linear
                    EvaluateNextPlacement(linearPlacement.PlacementRelTo,
                        out localPlacement, out linearPlacement);
                }
            }

            return accumulated ?? new XLocation();
        }

        private static XLocation ComposeLocation(XLocation? accumulated, XLocation stepLocation)
        {
            if (accumulated == null)
                return stepLocation;

            var composed = (XLocation)stepLocation.Multiplied(accumulated);
            accumulated.Dispose();
            stepLocation.Dispose();
            return composed;
        }

        private static void EvaluateNextPlacement(
            IIfcObjectPlacement? placementRelTo,
            out IIfcLocalPlacement? nextLocal,
            out IfcLinearPlacement? nextLinear)
        {
            if (placementRelTo is IIfcLocalPlacement lp)
            {
                nextLocal = lp;
                nextLinear = null;
            }
            else if (placementRelTo is IfcLinearPlacement linP)
            {
                nextLocal = null;
                nextLinear = linP;
            }
            else
            {
                nextLocal = null;
                nextLinear = null;
            }
        }
    }
}
