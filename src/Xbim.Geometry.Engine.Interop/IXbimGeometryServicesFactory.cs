using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Geometry.Engine.Interop
{
    /// <summary>
    /// A factory used to construct a native Geometry engine and associated resources
    /// </summary>
    public interface IXbimGeometryServicesFactory
    {
        /// <summary>
        /// Gets the native <see cref="IXGeometryConverterFactory"/>
        /// </summary>
        IXGeometryConverterFactory GeometryConverterFactory { get; }

        /// <summary>
        /// Creates a new native Geometry Engine for the provided model
        /// </summary>
        IXbimGeometryEngine CreateGeometryEngine(IModel model, ILoggerFactory loggerFactory);

        /// <summary>
        /// Creates a low level <see cref="IXModelGeometryService"/> providing low level native access to Geometry services
        /// </summary>
        IXModelGeometryService CreateModelGeometryService(IModel model, ILoggerFactory loggerFactory);
    }
}
