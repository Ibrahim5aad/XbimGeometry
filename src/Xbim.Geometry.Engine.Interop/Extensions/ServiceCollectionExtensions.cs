using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop;
using Xbim.Geometry.Engine.Interop.Configuration;
using Xbim.Geometry.Engine.Interop.Factories;
using Xbim.Geometry.Engine.Interop.Services;
using Xbim.Ifc4.Interfaces;

namespace Xbim.Common.Configuration
{
    /// <summary>
    /// ServiceCollection Extensions for xbim Geometry
    /// </summary>
    public static class ServiceCollectionExtensions
    {

        /// <summary>
        /// Adds xbim geometry services to the specified <see cref="IServiceCollection"/>
        /// </summary>
        /// <param name="services"></param>
        /// <returns></returns>
        [Obsolete("Prefer services.AddXbimToolkit(c => c.AddGeometryServices()) instead")]
        public static IServiceCollection AddXbimGeometryServices(this IServiceCollection services)
        {
            return services.AddXbimGeometryServicesInternal(delegate { });
        }

        /// <summary>
        /// Adds xbim geometry services to the specified <see cref="IServiceCollection"/>
        /// </summary>
        /// <param name="services"></param>
        /// <returns></returns>
        internal static IServiceCollection AddXbimGeometryServicesInternal(this IServiceCollection services)
        {
            return services.AddXbimGeometryServicesInternal(delegate { });
        }


        /// <summary>
        /// Adds xbim geometry services to the specified <see cref="IServiceCollection"/>
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configure"></param>
        /// <returns>The <see cref="IServiceCollection"/> so additional calls can be chained</returns>
        [Obsolete("Prefer services.AddXbimToolkit(c => c.AddGeometryServices()) instead")]
        public static IServiceCollection AddXbimGeometryServices(this IServiceCollection services, Action<IGeometryEngineBuilder> configure)
        {
            return AddXbimGeometryServicesInternal(services, configure);
        }

        /// <summary>
        /// Adds xbim geometry services to the specified <see cref="IServiceCollection"/>
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configure"></param>
        /// <returns>The <see cref="IServiceCollection"/> so additional calls can be chained</returns>
        internal static IServiceCollection AddXbimGeometryServicesInternal(this IServiceCollection services, Action<IGeometryEngineBuilder> configure)
        {
            services.AddOptions();

            // We want the same instance of GE regardless of interface used. This 'forwarding factory' is the only approach current M.E.DI
            services.TryAddScoped<XbimGeometryEngine>();
            services.TryAddScoped<IXbimGeometryEngine>(x => x.GetRequiredService<XbimGeometryEngine>());
            services.TryAddScoped<IXbimManagedGeometryEngine>(x => x.GetRequiredService<XbimGeometryEngine>());
            services.AddFactory<IXbimManagedGeometryEngine>();

            services.TryAddSingleton<XbimGeometryEngineFactory>();

            // Register the native geometry converter factory directly — no reflection or assembly loading needed
            services.TryAddSingleton<IXGeometryConverterFactory, GeometryConverterFactory>();

            // Shape service for boolean operations, placement, serialization, and meshing
            services.TryAddSingleton<IXShapeService>(sp =>
                new ShapeService(sp.GetRequiredService<ILoggerFactory>()));

            // Geometry primitives factory (points, directions, locations, matrices, bounding boxes)
            services.TryAddSingleton<IXGeometryPrimitives, GeometryPrimitives>();

            configure(new GeometryEngineBuilder(services));
            return services;
        }


        /// <summary>
        /// Shorthand for adding service of <see cref="Func{T, TResult}"/> where TResult is<typeparamref name="TService"/>
        /// </summary>
        /// <typeparam name="TService"></typeparam>
        /// <param name="serviceCollection"></param>
        /// <returns>The <see cref="IServiceCollection"/></returns>
        internal static IServiceCollection AddFactory<TService>(this IServiceCollection serviceCollection)
            where TService : class

        {
            serviceCollection
                .TryAddSingleton<Func<TService>>(sp => sp.GetRequiredService<TService>);
            return serviceCollection;
        }
    }
}
