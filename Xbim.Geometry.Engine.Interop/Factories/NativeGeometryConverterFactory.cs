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
    public class NativeGeometryConverterFactory : IXGeometryConverterFactory
    {
        /// <inheritdoc/>
        public IXModelGeometryService CreateModelGeometryService(IModel model, ILoggerFactory loggerFactory)
        {
            return new NativeModelGeometryService(model, loggerFactory);
        }

        /// <inheritdoc/>
        public IXbimGeometryEngine CreateGeometryEngineV5(IModel model, ILoggerFactory loggerFactory)
        {
            throw new PlatformNotSupportedException(
                "V5 geometry engine is not available. Use V6 or CreateModelGeometryService instead.");
        }

        /// <inheritdoc/>
        public IXGeometryEngineV6 CreateGeometryEngineV6(IModel model, ILoggerFactory loggerFactory)
        {
            var service = new NativeModelGeometryService(model, loggerFactory);
            return new NativeGeometryEngineV6(service, loggerFactory);
        }

        /// <inheritdoc/>
        public IXbimGeometryEngine CreateGeometryEngine(XGeometryEngineVersion version, IModel model, ILoggerFactory loggerFactory)
        {
            return version switch
            {
                XGeometryEngineVersion.V5 => CreateGeometryEngineV5(model, loggerFactory),
                XGeometryEngineVersion.V6 => CreateGeometryEngineV6(model, loggerFactory),
                _ => throw new ArgumentOutOfRangeException(nameof(version), version, "Unsupported geometry engine version.")
            };
        }

        /// <inheritdoc/>
        public IXModelGeometryService GetUnderlyingModelGeometryService(IXbimGeometryEngine geometryEngine)
        {
            if (geometryEngine is NativeGeometryEngineV6 v6)
                return v6.ModelGeometryService;

            throw new InvalidOperationException(
                $"Cannot extract model geometry service from engine type {geometryEngine.GetType().Name}.");
        }
    }
}
