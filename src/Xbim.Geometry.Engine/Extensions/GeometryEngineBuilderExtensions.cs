using Microsoft.Extensions.DependencyInjection;
using System;
using Xbim.Geometry.Engine.Configuration;


namespace Xbim.Common.Configuration
{
    /// <summary>
    /// Extension methods for <see cref="IGeometryEngineBuilder"/>
    /// </summary>
    public static class GeometryEngineBuilderExtensions
    {
        /// <summary>
        /// Configure the <paramref name="builder"/> with the <see cref="GeometryEngineOptions"/>.
        /// </summary>
        /// <param name="builder">The <see cref="IGeometryEngineBuilder"/> to be configured with <see cref="GeometryEngineOptions"/></param>
        /// <param name="action">The action used to configure the logger factory</param>
        /// <returns>The <see cref="IGeometryEngineBuilder"/> so that additional calls can be chained.</returns>
        public static IGeometryEngineBuilder Configure(this IGeometryEngineBuilder builder, Action<GeometryEngineOptions> action)
        {
            builder.Services.Configure(action);
            return builder;
        }
    }
}
