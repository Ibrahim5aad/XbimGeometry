using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Engine.Primitives;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Engine.Shapes;
using Xbim.Geometry.Exceptions;

namespace Xbim.Geometry.Engine.Factories
{
    /// <summary>
    /// Computes 2D footprints and outlines from 3D shapes using HLR projection.
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

        private NativeContextHandle ContextHandle => _modelService.ContextHandle;

        public IXFootprint CreateFootprint(IXShape shape, double linearDeflection,
            double angularDeflection = 0.52359877559829887307710723054658,
            bool createExactFootprint = true)
        {
            if (shape is not XbimShape nativeShape)
                throw new ArgumentException("Shape must be a native XbimShape instance.", nameof(shape));

            if (!shape.IsValidShape())
                throw new XbimGeometryServiceException("Cannot create footprint: shape is invalid.");

            // createExactFootprint=true means use exact HLR (useHlrPolyAlgo=0)
            // createExactFootprint=false means use polyhedral HLR (useHlrPolyAlgo=1)
            int usePolyAlgo = createExactFootprint ? 0 : 1;

            int result = XbimGeometryNativeApi.xbim_projection_create_footprint(
                ContextHandle,
                nativeShape.Handle,
                linearDeflection,
                angularDeflection,
                _modelService.Precision,
                usePolyAlgo,
                out IntPtr buffer,
                out int bufferLen);

            if (result != 0)
            {
                throw new XbimGeometryServiceException(
                    $"Footprint creation failed: {XbimGeometryNativeApi.GetLastError()}");
            }

            try
            {
                return new XFootprint(buffer, bufferLen);
            }
            finally
            {
                XbimGeometryNativeApi.xbim_projection_free_buffer(buffer);
            }
        }

        public IXFootprint CreateFootprint(IXShape shape, bool createExactFootprint = true)
        {
            double oneMillimeter = _modelService.OneMillimeter;
            bool isMetric = oneMillimeter == 1 || oneMillimeter == 0.001 ||
                            oneMillimeter == 0.000001 || oneMillimeter == 0.01 ||
                            oneMillimeter == 0.1 || oneMillimeter == 10 ||
                            oneMillimeter == 100 || oneMillimeter == 0.00001 ||
                            oneMillimeter == 0.0001;

            double deflection = isMetric ? oneMillimeter * 25 : oneMillimeter * 25.4;
            double angularDeflection = Math.PI / 6;

            return CreateFootprint(shape, deflection, angularDeflection, createExactFootprint);
        }

        public IXCompound GetOutline(IXShape shape)
        {
            if (shape is not XbimShape nativeShape)
                throw new ArgumentException("Shape must be a native XbimShape instance.", nameof(shape));

            if (!shape.IsValidShape())
                throw new XbimGeometryServiceException("Cannot get outline: shape is invalid.");

            int result = XbimGeometryNativeApi.xbim_projection_get_outline(
                nativeShape.Handle,
                out NativeShapeHandle outHandle);

            if (result != 0)
            {
                throw new XbimGeometryServiceException(
                    $"Outline creation failed: {XbimGeometryNativeApi.GetLastError()}");
            }

            return (IXCompound)NativeShapeWrapper.WrapShape(outHandle);
        }

        public IEnumerable<IXFace> CreateSection(IXShape shape, IXPlane cutPlane)
        {
            throw new NotImplementedException(
                "Section cutting requires native BRepAlgoAPI_Section algorithm export.");
        }
    }
}
