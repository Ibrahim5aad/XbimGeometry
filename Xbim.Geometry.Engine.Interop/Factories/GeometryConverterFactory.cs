using System;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop.Factories
{
    /// <summary>
    /// Creates geometry engine instances and model-level geometry services
    /// for processing IFC geometry through the native OCCT kernel.
    /// </summary>
    public class GeometryConverterFactory : IXGeometryConverterFactory
    {
        /// <inheritdoc/>
        public IXModelGeometryService CreateModelGeometryService(IModel model, ILoggerFactory loggerFactory)
        {
            return new ModelGeometryService(model, loggerFactory);
        }

        /// <inheritdoc/>
        public IXbimGeometryEngine CreateGeometryEngineV5(IModel model, ILoggerFactory loggerFactory)
        {
            var service = new ModelGeometryService(model, loggerFactory);
            return new GeometryEngine(service, loggerFactory);
        }

        /// <inheritdoc/>
        public IXGeometryEngineV6 CreateGeometryEngineV6(IModel model, ILoggerFactory loggerFactory)
        {
            var service = new ModelGeometryService(model, loggerFactory);
            return new GeometryEngine(service, loggerFactory);
        }

        /// <inheritdoc/>
        public IXbimGeometryEngine CreateGeometryEngine(XGeometryEngineVersion version, IModel model, ILoggerFactory loggerFactory)
        {
            return CreateGeometryEngineV6(model, loggerFactory);
        }
        
        /// <inheritdoc/>
        public IXModelGeometryService GetUnderlyingModelGeometryService(IXbimGeometryEngine geometryEngine)
        {
            if (geometryEngine is GeometryEngine v6)
                return v6.ModelGeometryService;

            throw new InvalidOperationException(
                $"Cannot extract model geometry service from engine type {geometryEngine.GetType().Name}.");
        }
    }
}
