using Microsoft.Extensions.Logging;
using Xbim.Common;
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
        public IXbimGeometryEngine CreateGeometryEngine(IModel model, ILoggerFactory loggerFactory)
        {
            return new XbimGeometryEngine(model, loggerFactory);
        }
    }
}
