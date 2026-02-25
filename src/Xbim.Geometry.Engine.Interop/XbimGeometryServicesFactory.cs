using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop
{
    /// <summary>
    /// A factory providing access to the native Geometry Engine and related services
    /// </summary>
    public class XbimGeometryServicesFactory : IXbimGeometryServicesFactory
    {

        /// <inheritdoc/>
        public IXGeometryConverterFactory GeometryConverterFactory { get; }

        /// <inheritdoc/>
        public XbimGeometryServicesFactory(IXGeometryConverterFactory xGeometryConverterFactory)
        {
            GeometryConverterFactory = xGeometryConverterFactory;
        }

        /// <inheritdoc/>
        public IXModelGeometryService CreateModelGeometryService(IModel model, ILoggerFactory loggerFactory)
        {
            return GeometryConverterFactory.CreateModelGeometryService(model, loggerFactory);
        }

        /// <inheritdoc/>
        public IXbimGeometryEngine CreateGeometryEngine(IModel model, ILoggerFactory loggerFactory)
        {
            return GeometryConverterFactory.CreateGeometryEngine(model, loggerFactory);
        }
    }
}
