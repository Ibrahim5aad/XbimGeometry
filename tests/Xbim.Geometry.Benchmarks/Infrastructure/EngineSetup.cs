using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Common.Configuration;
using Xbim.Common.Geometry;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop;
using Xbim.Geometry.Engine.Interop.Configuration;
using Xbim.Ifc4.Interfaces;
using Xbim.IO.Memory;

namespace Xbim.Geometry.Benchmarks.Infrastructure;

/// <summary>
/// Shared engine initialization for benchmarks. Works identically with both
/// old (NuGet) and new (P/Invoke) engines through the shared IXbimGeometryEngine interface.
/// </summary>
public static class EngineSetup
{
    private static readonly Lazy<ILoggerFactory> _loggerFactory = new(() =>
        Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));

    private static bool _initialized;

    public static ILoggerFactory LoggerFactory => _loggerFactory.Value;

    /// <summary>
    /// Ensures XbimServices are configured. Safe to call multiple times.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (_initialized) return;
        if (!XbimServices.Current.IsBuilt)
        {
            XbimServices.Current.ConfigureServices(s => s
                .AddXbimToolkit(c => c
                    .AddMemoryModel()
                    .AddGeometryServices()));
        }
        _initialized = true;
    }

    /// <summary>
    /// Creates an IXbimGeometryEngine for the given model using the specified engine version.
    /// For old engine: V5 and V6 use different code paths.
    /// For new engine: V5 and V6 are unified (same result).
    /// </summary>
    public static IXbimGeometryEngine CreateEngine(IModel model, XGeometryEngineVersion version)
    {
        EnsureInitialized();
        var options = new GeometryEngineOptions { GeometryEngineVersion = version };
        return new XbimGeometryEngine(model, LoggerFactory, options);
    }

    /// <summary>
    /// Opens an IFC file as a MemoryModel.
    /// </summary>
    public static MemoryModel OpenModel(string testFilePath)
    {
        var fullPath = TestFileHelper.Resolve(testFilePath);
        return MemoryModel.OpenRead(fullPath);
    }
}
