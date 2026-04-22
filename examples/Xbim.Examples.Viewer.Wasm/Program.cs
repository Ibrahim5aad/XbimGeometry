using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xbim.Common.Configuration;
using Xbim.Examples.Viewer.Wasm;
using Xbim.Geometry.Viewer;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Let xbim Trace/Debug messages through (they map to console.debug in the browser —
// enable "Verbose" in Chrome DevTools console filter to see them).
builder.Logging.AddFilter("Xbim", LogLevel.Trace);

// Enable IFC file processing in the FileLoaderPanel
builder.Services.AddSingleton<IIfcProcessingService, WasmIfcProcessingService>();

// Theme service (shared across components)
builder.Services.AddSingleton<ViewerThemeService>();

var host = builder.Build();

// Wire xbim geometry services with the Blazor logger (routes to browser console).
// Without this, xbim falls back to NullLoggerFactory and discards all logs.
var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
XbimServices.Current.ConfigureServices(services => services
    .AddXbimToolkit(opt => opt
        .AddLoggerFactory(loggerFactory)
        .AddGeometryServices()));

await host.RunAsync();
