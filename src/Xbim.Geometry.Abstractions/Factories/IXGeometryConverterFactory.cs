using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Abstractions
{
    public interface IXGeometryConverterFactory
    {
        /// <summary>
        /// Creates a geometry engine instance for the given model
        /// </summary>
        IXbimGeometryEngine CreateGeometryEngine(IModel model, ILoggerFactory loggerFactory);

        /// <summary>
        /// Creates a Root Service to access Geometry Factories that are scoped to the current Model
        /// </summary>
        IXModelGeometryService CreateModelGeometryService(IModel model, ILoggerFactory loggerFactory);

        /// <summary>
        /// Gets the underlying model geometry service used by this engine
        /// </summary>
        IXModelGeometryService GetUnderlyingModelGeometryService(IXbimGeometryEngine geometryEngine);
    }
}
