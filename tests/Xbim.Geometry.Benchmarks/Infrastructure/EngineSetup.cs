using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xbim.Common;
using Xbim.Common.Configuration;
using Xbim.Common.Geometry;
#if OLD_ENGINE
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Interop;
using Xbim.Geometry.Engine.Interop.Configuration;
#else
using Xbim.Geometry.Engine;
using Xbim.Geometry.Engine.Configuration;
#endif
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

#if OLD_ENGINE
    /// <summary>
    /// Creates an IXbimGeometryEngine for the given model using the specified engine version.
    /// V5 and V6 use different code paths in the old engine.
    /// </summary>
    public static IXbimGeometryEngine CreateEngine(IModel model, XGeometryEngineVersion version)
    {
        EnsureInitialized();
        var options = new GeometryEngineOptions { GeometryEngineVersion = version };
        return new XbimGeometryEngine(model, LoggerFactory, options);
    }
#else
    /// <summary>
    /// Creates an IXbimGeometryEngine for the given model.
    /// </summary>
    public static IXbimGeometryEngine CreateEngine(IModel model)
    {
        EnsureInitialized();
        return new XbimGeometryEngine(model, LoggerFactory);
    }
#endif

    /// <summary>
    /// Opens an IFC file as a MemoryModel.
    /// </summary>
    public static MemoryModel OpenModel(string testFilePath)
    {
        var fullPath = TestFileHelper.Resolve(testFilePath);
        return MemoryModel.OpenRead(fullPath);
    }
}
