using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xbim.Common.Configuration;
using Xbim.Ifc;
using Xunit.DependencyInjection;
using Xunit.DependencyInjection.Logging;
namespace Xbim.Geometry.Engine.Tests
{
    public class Startup
    {
#if DEBUG

        const LogLevel DefaultLogLevel = LogLevel.Debug;
#else
        const LogLevel DefaultLogLevel = LogLevel.Information;
#endif
        public void Configure(ILoggerFactory loggerFactory, IServiceProvider serviceProvider)
        {

          // Nothing to do

        }

        public void ConfigureServices(IServiceCollection services)
        {

            services
                .AddLogging(configure => configure
                    .SetMinimumLevel(DefaultLogLevel)
                    .AddXunitOutput()
                    .AddConsole())
                .AddXbimToolkit(configure => configure
                    .AddMemoryModel()
                    .AddGeometryServices()
                    )
                ;


            // Re-use this Service Collection in the internal xbim DI
            // We can't substitute Xunit.DependencyInjection's IServiceProvider directly since it's a scoped provider which has a per test lifetime
            // and we need the root ServiceProvider. This means we have two ServiceProvider instances in the tests.

            XbimServices.Current.UseExternalServiceCollection(services);

            // Ibrahim: Force IfcStore's static constructor to run now (single-threaded, before parallel test execution).
            // Without this, a TOCTOU race exists: IfcStore..cctor checks IsBuilt then calls ConfigureServices,
            // but a parallel test can trigger XbimServices.Current.ServiceProvider (setting isBuilt=true) between
            // those two steps, causing ConfigureServices to throw.
            RuntimeHelpers.RunClassConstructor(typeof(IfcStore).TypeHandle);

        }
    }

}
