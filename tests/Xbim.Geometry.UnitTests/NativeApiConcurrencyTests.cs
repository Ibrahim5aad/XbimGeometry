using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Engine.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Xbim.Geometry.Engine.Tests;

/// <summary>
/// Exercises the native P/Invoke layer from multiple C# threads to verify
/// thread safety of handle lifecycle, error reporting, and geometry operations.
/// </summary>
public class NativeApiConcurrencyTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;

    private const int ThreadCount = 4;
    private const int IterationsPerThread = 50;

    // XbimShapeType enum values (from xbim_geometry_api.h)
    private const int ShapeTypeSolid = 5;  // XBIM_SHAPE_SOLID

    public NativeApiConcurrencyTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
    }

    public void Dispose() => _loggerFactory.Dispose();

    /// <summary>
    /// Creates a <see cref="ModelGeometryService"/> with a millimetre-based mock model.
    /// </summary>
    private ModelGeometryService CreateService()
    {
        return new ModelGeometryService(IfcMoq.ModelMock(), _loggerFactory);
    }

    /// <summary>
    /// Builds a unit block (1x1x1 at origin) on the given context.
    /// </summary>
    private static NativeShapeHandle BuildBlock(
        NativeContextHandle ctx,
        double xLen = 1, double yLen = 1, double zLen = 1,
        double originX = 0, double originY = 0, double originZ = 0)
    {
        int result = XbimGeometryNativeApi.xbim_solid_build_block(
            ctx,
            originX, originY, originZ,
            0, 0, 1,  // Z direction
            1, 0, 0,  // X direction
            xLen, yLen, zLen,
            out var handle);

        if (result != 0)
            throw new InvalidOperationException(
                $"xbim_solid_build_block failed: {XbimGeometryNativeApi.GetLastError()}");

        return handle;
    }

    #region Independent contexts — each thread owns everything

    [Fact]
    public void IndependentContexts_BuildAndDestroyShapes_NoContention()
    {
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, ThreadCount, _ =>
        {
            try
            {
                using var service = CreateService();
                var ctx = service.ContextHandle;

                for (int i = 0; i < IterationsPerThread; i++)
                {
                    using var shape = BuildBlock(ctx);
                    shape.IsInvalid.Should().BeFalse();

                    int typeResult = XbimGeometryNativeApi.xbim_shape_type(shape, out int shapeType);
                    typeResult.Should().Be(0);
                    shapeType.Should().Be(ShapeTypeSolid);
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        exceptions.Should().BeEmpty();
    }

    [Fact]
    public void IndependentContexts_BooleanOperations_NoContention()
    {
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, ThreadCount, _ =>
        {
            try
            {
                using var service = CreateService();
                var ctx = service.ContextHandle;

                for (int i = 0; i < IterationsPerThread; i++)
                {
                    using var body = BuildBlock(ctx, 10, 10, 10);
                    using var tool = BuildBlock(ctx, 5, 5, 5);

                    int cutResult = XbimGeometryNativeApi.xbim_boolean_cut(
                        ctx, body, tool, 0, out _, out var resultShape);
                    cutResult.Should().Be(0);

                    using (resultShape)
                    {
                        resultShape.IsInvalid.Should().BeFalse();

                        int volResult = XbimGeometryNativeApi.xbim_shape_volume(
                            resultShape, out double volume);
                        volResult.Should().Be(0);
                        volume.Should().BeApproximately(875, 1.0);
                    }
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        exceptions.Should().BeEmpty();
    }

    [Fact]
    public void IndependentContexts_MeshOperations_NoContention()
    {
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, ThreadCount, _ =>
        {
            try
            {
                using var service = CreateService();
                var ctx = service.ContextHandle;

                for (int i = 0; i < 10; i++) // mesh is heavier, fewer iterations
                {
                    using var shape = BuildBlock(ctx, 10, 10, 10);

                    int meshResult = XbimGeometryNativeApi.xbim_mesh_create_wexbim(
                        ctx, shape,
                        1e-3,  // tolerance
                        0.5,   // linear deflection
                        0.5,   // angular deflection
                        1.0,   // scale
                        0,     // checkEdges
                        out IntPtr buffer, out int bufferSize,
                        out int _hasCurves,
                        out double _minX, out double _minY, out double _minZ,
                        out double _maxX, out double _maxY, out double _maxZ);

                    meshResult.Should().Be(0);
                    buffer.Should().NotBe(IntPtr.Zero);
                    bufferSize.Should().BeGreaterThan(0);

                    XbimGeometryNativeApi.xbim_buffer_free(buffer);
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        exceptions.Should().BeEmpty();
    }

    #endregion

    #region Shared context — one ModelGeometryService, multiple threads

    [Fact]
    public void SharedContext_ConcurrentShapeBuilding_IsThreadSafe()
    {
        using var service = CreateService();
        var ctx = service.ContextHandle;
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, ThreadCount * IterationsPerThread, _ =>
        {
            try
            {
                using var shape = BuildBlock(ctx);
                shape.IsInvalid.Should().BeFalse();

                int typeResult = XbimGeometryNativeApi.xbim_shape_type(shape, out int shapeType);
                typeResult.Should().Be(0);
                shapeType.Should().Be(ShapeTypeSolid);
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        exceptions.Should().BeEmpty();
    }

    [Fact]
    public void SharedContext_ConcurrentBooleanCuts_IsThreadSafe()
    {
        using var service = CreateService();
        var ctx = service.ContextHandle;
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, ThreadCount * IterationsPerThread, _ =>
        {
            try
            {
                using var body = BuildBlock(ctx, 10, 10, 10);
                using var tool = BuildBlock(ctx, 5, 5, 5);

                int cutResult = XbimGeometryNativeApi.xbim_boolean_cut(
                    ctx, body, tool, 0, out _, out var resultShape);

                using (resultShape)
                {
                    cutResult.Should().Be(0);
                    resultShape.IsInvalid.Should().BeFalse();

                    int volResult = XbimGeometryNativeApi.xbim_shape_volume(
                        resultShape, out double volume);
                    volResult.Should().Be(0);
                    volume.Should().BeApproximately(875, 1.0);
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        exceptions.Should().BeEmpty();
    }

    [Fact]
    public void SharedContext_MixedOperations_IsThreadSafe()
    {
        using var service = CreateService();
        var ctx = service.ContextHandle;
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, ThreadCount, threadIndex =>
        {
            try
            {
                for (int i = 0; i < IterationsPerThread; i++)
                {
                    int op = (threadIndex + i) % 4;
                    switch (op)
                    {
                        case 0: // Build solid
                            using (var shape = BuildBlock(ctx, 5, 5, 5))
                            {
                                XbimGeometryNativeApi.xbim_shape_type(shape, out int t);
                                t.Should().Be(ShapeTypeSolid);
                            }
                            break;

                        case 1: // Boolean cut
                            using (var body = BuildBlock(ctx, 10, 10, 10))
                            using (var tool = BuildBlock(ctx, 3, 3, 3))
                            {
                                XbimGeometryNativeApi.xbim_boolean_cut(
                                    ctx, body, tool, 0, out _, out var cut);
                                cut.Dispose();
                            }
                            break;

                        case 2: // Boolean union
                            using (var s1 = BuildBlock(ctx, 5, 5, 5))
                            using (var s2 = BuildBlock(ctx, 5, 5, 5, originX: 3))
                            {
                                XbimGeometryNativeApi.xbim_boolean_union(
                                    ctx, s1, s2, 0, out _, out var uni);
                                uni.Dispose();
                            }
                            break;

                        case 3: // Compound
                            using (var c1 = BuildBlock(ctx, 2, 2, 2))
                            using (var c2 = BuildBlock(ctx, 3, 3, 3, originX: 5))
                            {
                                var arr = new NativeHandleArray(new SafeHandle[] { c1, c2 });
                                try
                                {
                                    XbimGeometryNativeApi.xbim_compound_make(
                                        ctx, arr.Ptrs, arr.Length, out var compound);
                                    compound.Dispose();
                                }
                                finally { arr.Dispose(); }
                            }
                            break;
                    }
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        exceptions.Should().BeEmpty();
    }

    #endregion

    #region Thread-local error isolation

    [Fact]
    public async Task ErrorMessages_AreIsolatedPerThread()
    {
        using var service = CreateService();
        var ctx = service.ContextHandle;
        var errors = new ConcurrentDictionary<int, string>();

        var barrier = new Barrier(2);

        var tasks = new[]
        {
            Task.Run(() =>
            {
                // Thread 0: trigger an error with zero-direction line
                XbimGeometryNativeApi.xbim_curve_build_line_3d(
                    ctx, 0, 0, 0, 0, 0, 0, out var badCurve);
                if (!badCurve.IsInvalid) badCurve.Dispose();

                barrier.SignalAndWait(); // sync point: both threads have their error
                barrier.SignalAndWait(); // sync point: now read errors

                errors[0] = XbimGeometryNativeApi.GetLastError();
            }),
            Task.Run(() =>
            {
                // Thread 1: succeed — build a valid block, no error
                using var shape = BuildBlock(ctx);
                shape.IsInvalid.Should().BeFalse();

                barrier.SignalAndWait(); // sync point
                barrier.SignalAndWait(); // sync point

                errors[1] = XbimGeometryNativeApi.GetLastError();
            })
        };

        await Task.WhenAll(tasks);

        // Thread 0 should have a non-empty error from the failed line call
        errors[0].Should().NotBeNullOrEmpty("thread 0 triggered a native error");

        // Thread 1 should have empty error — not polluted by thread 0's failure
        errors[1].Should().BeEmpty("thread 1 succeeded, error must not leak from thread 0");
    }

    #endregion

    #region Handle lifecycle under contention

    [Fact]
    public void HandleDisposal_UnderContention_DoesNotCorrupt()
    {
        using var service = CreateService();
        var ctx = service.ContextHandle;
        var exceptions = new ConcurrentBag<Exception>();

        // Pre-build shapes, then dispose them concurrently
        var shapes = new NativeShapeHandle[ThreadCount * 10];
        for (int i = 0; i < shapes.Length; i++)
            shapes[i] = BuildBlock(ctx, 1 + i % 5, 1 + i % 5, 1 + i % 5);

        Parallel.For(0, shapes.Length, i =>
        {
            try
            {
                shapes[i].Dispose();
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        exceptions.Should().BeEmpty();
    }

    [Fact]
    public void LocationHandles_ConcurrentCreateAndDispose_IsThreadSafe()
    {
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, ThreadCount * IterationsPerThread, _ =>
        {
            try
            {
                int result = XbimGeometryNativeApi.xbim_location_create_from_axis2(
                    0, 0, 0,    // origin
                    0, 0, 1,    // Z direction
                    1, 0, 0,    // X direction
                    out var loc);
                result.Should().Be(0);

                using (loc)
                {
                    loc.IsInvalid.Should().BeFalse();

                    int invResult = XbimGeometryNativeApi.xbim_location_invert(
                        loc, out var inverted);
                    invResult.Should().Be(0);
                    inverted.Dispose();
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        exceptions.Should().BeEmpty();
    }

    #endregion

    #region Shared context — factory-like patterns (mimics NativeModelGeometryService)

    [Fact]
    public void SharedContext_BuildAndTransform_IsThreadSafe()
    {
        using var service = CreateService();
        var ctx = service.ContextHandle;
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, ThreadCount * IterationsPerThread, _ =>
        {
            try
            {
                using var shape = BuildBlock(ctx, 5, 5, 5);

                // Create a transform location
                int locResult = XbimGeometryNativeApi.xbim_location_create_from_axis2(
                    10, 20, 30,  // translated origin
                    0, 0, 1,
                    1, 0, 0,
                    out var location);
                locResult.Should().Be(0);

                using (location)
                {
                    int moveResult = XbimGeometryNativeApi.xbim_shape_moved(
                        shape, location, out var moved);
                    moveResult.Should().Be(0);

                    using (moved)
                    {
                        moved.IsInvalid.Should().BeFalse();

                        // Verify bounding box is shifted
                        int bbResult = XbimGeometryNativeApi.xbim_shape_bounding_box(
                            moved,
                            out double minX, out double bbMinY, out double bbMinZ,
                            out double maxX, out double bbMaxY, out double bbMaxZ);
                        bbResult.Should().Be(0);
                        minX.Should().BeApproximately(10, 0.01);
                        maxX.Should().BeApproximately(15, 0.01);
                    }
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        exceptions.Should().BeEmpty();
    }

    [Fact]
    public void SharedContext_ProfileAndExtrude_IsThreadSafe()
    {
        using var service = CreateService();
        var ctx = service.ContextHandle;
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, ThreadCount * 10, _ =>
        {
            try
            {
                // Build a rectangular profile face
                int profResult = XbimGeometryNativeApi.xbim_profile_build_rectangle(
                    ctx,
                    0, 0, 0,  // origin
                    0, 0, 1,  // Z
                    1, 0, 0,  // X
                    4, 3,     // 4x3 rectangle
                    out var profileFace);
                profResult.Should().Be(0);

                using (profileFace)
                {
                    // Extrude it
                    int extResult = XbimGeometryNativeApi.xbim_solid_build_extruded(
                        ctx,
                        profileFace,
                        0, 0, 1,  // extrude along Z
                        5,        // depth
                        NativeLocationHandle.NullHandle,
                        out var solid);
                    extResult.Should().Be(0);

                    using (solid)
                    {
                        solid.IsInvalid.Should().BeFalse();

                        int volResult = XbimGeometryNativeApi.xbim_shape_volume(
                            solid, out double volume);
                        volResult.Should().Be(0);
                        volume.Should().BeApproximately(60, 0.1); // 4*3*5
                    }
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        exceptions.Should().BeEmpty();
    }

    [Fact]
    public void SharedContext_CurveOperations_IsThreadSafe()
    {
        using var service = CreateService();
        var ctx = service.ContextHandle;
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, ThreadCount * IterationsPerThread, _ =>
        {
            try
            {
                // Build a 3D line curve
                int lineResult = XbimGeometryNativeApi.xbim_curve_build_line_3d(
                    ctx,
                    0, 0, 0,    // origin
                    1, 0, 0,    // direction along X
                    out var line);
                lineResult.Should().Be(0);

                using (line)
                {
                    line.IsInvalid.Should().BeFalse();

                    // Query parameters
                    int paramResult = XbimGeometryNativeApi.xbim_curve_parameters(
                        line, out double first, out double last);
                    paramResult.Should().Be(0);

                    // Evaluate at midpoint
                    double mid = (first + last) / 2;
                    int valResult = XbimGeometryNativeApi.xbim_curve_value(
                        line, mid, out double x, out double y, out double z);
                    valResult.Should().Be(0);
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        exceptions.Should().BeEmpty();
    }

    #endregion

    #region Stress: high-volume handle churn

    [Fact]
    public void StressTest_RapidCreateDispose_DoesNotLeak()
    {
        using var service = CreateService();
        var ctx = service.ContextHandle;
        var exceptions = new ConcurrentBag<Exception>();

        // Many threads rapidly creating and destroying shapes
        Parallel.For(0, ThreadCount, _ =>
        {
            try
            {
                for (int i = 0; i < 200; i++)
                {
                    using var shape = BuildBlock(ctx, 1, 1, 1);
                    // No assertions — just verifying no crash or hang
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        exceptions.Should().BeEmpty();

        // If we get here without crash, handles were managed correctly.
        // A manual GC pass exercises the SafeHandle release path for any
        // handles that might have escaped deterministic disposal.
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    #endregion

    #region Logging callback under concurrent native calls

    [Fact]
    public void SharedContext_LogCallbackSurvivesConcurrentCalls()
    {
        var logger = new ConcurrentLogger();
        using var loggerFactory = new SingleLoggerFactory(logger);
        using var service = new ModelGeometryService(IfcMoq.ModelMock(), loggerFactory);
        var ctx = service.ContextHandle;
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, ThreadCount, _ =>
        {
            try
            {
                for (int i = 0; i < IterationsPerThread; i++)
                {
                    // Alternate between success and failure calls to exercise
                    // the logging callback from multiple threads
                    if (i % 3 == 0)
                    {
                        // Trigger a native warning via zero-direction
                        XbimGeometryNativeApi.xbim_curve_build_line_3d(
                            ctx, 0, 0, 0, 0, 0, 0, out var bad);
                        if (!bad.IsInvalid) bad.Dispose();
                    }
                    else
                    {
                        using var shape = BuildBlock(ctx);
                    }
                }
            }
            catch (Exception ex) { exceptions.Add(ex); }
        });

        exceptions.Should().BeEmpty();
        logger.MessageCount.Should().BeGreaterThan(0,
            "some native failures should have generated log messages");
    }

    /// <summary>Thread-safe logger that counts messages.</summary>
    private sealed class ConcurrentLogger : ILogger
    {
        private int _count;
        public int MessageCount => _count;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Interlocked.Increment(ref _count);

        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    /// <summary>Routes all categories to one logger instance.</summary>
    private sealed class SingleLoggerFactory : ILoggerFactory
    {
        private readonly ILogger _logger;
        public SingleLoggerFactory(ILogger logger) => _logger = logger;
        public ILogger CreateLogger(string categoryName) => _logger;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
    }

    #endregion
}
