using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Engine.Internal;
using Xbim.Geometry.Engine.Services;
using Xbim.Geometry.Engine.Tests.Helpers;
using Xunit;

namespace Xbim.Geometry.Engine.Tests;

/// <summary>
/// Verifies that native log events reach the managed ILogger with the correct
/// log level and message, and that the callback delegate lifetime is managed correctly.
/// </summary>
public class LoggingCallbackTests : IDisposable
{
    private readonly FakeLogger _logger;
    private readonly LoggingService _loggingService;

    public LoggingCallbackTests()
    {
        _logger = new FakeLogger();
        _loggingService = new LoggingService(_logger);
    }

    public void Dispose() => _loggingService.Dispose();

    [Theory]
    [InlineData(1, LogLevel.Debug)]
    [InlineData(2, LogLevel.Information)]
    [InlineData(3, LogLevel.Warning)]
    [InlineData(4, LogLevel.Error)]
    [InlineData(5, LogLevel.Critical)]
    [InlineData(0, LogLevel.Debug)]   // unknown → falls through to Debug
    [InlineData(99, LogLevel.Debug)]
    public void Callback_MapsNativeLevel_ToCorrectLogLevel(int nativeLevel, LogLevel expectedLevel)
    {
        _loggingService.Callback(nativeLevel, "msg");

        _logger.Logs.Should().ContainSingle()
            .Which.Level.Should().Be(expectedLevel);
    }

    [Fact]
    public void Callback_ForwardsMessage_Verbatim()
    {
        const string msg = "Boolean operation failed: Standard_ConstructionError";

        _loggingService.Callback(3, msg);

        _logger.Logs.Single().Message.Should().Contain(msg);
    }

    [Fact]
    public void Callback_IsNotNull_BeforeDispose()
    {
        _loggingService.Callback.Should().NotBeNull();
    }

    [Fact]
    public void Dispose_FreesGCHandle_WithoutThrowing()
    {
        var svc = new LoggingService(new FakeLogger());
        var act = () => svc.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var svc = new LoggingService(new FakeLogger());
        var act = () => { svc.Dispose(); svc.Dispose(); };
        act.Should().NotThrow();
    }

    /// <summary>
    /// Verifies that the callback is wired into the native context during
    /// ModelGeometryService construction — if registration fails, the ctor throws.
    /// </summary>
    [Fact]
    public void ModelGeometryService_RegistersCallback_WithNativeContext()
    {
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        var model = IfcMoq.ModelMock();

        var act = () =>
        {
            using var service = new ModelGeometryService(model, loggerFactory);
            service.LoggingService.Should().NotBeNull();
        };

        act.Should().NotThrow();
    }

    #region OCCT round-trip: native failures reach the managed logger

    /// <summary>
    /// gp_Dir(0,0,0) throws Standard_ConstructionError inside native code.
    /// The catch block calls xbim_log_occt_failure, which must arrive at the
    /// managed ILogger as a Warning.
    /// </summary>
    [Fact]
    public void NativeOcctFailure_ZeroDirectionLine_LogsWarning()
    {
        var logger = new FakeLogger();
        using var service = new ModelGeometryService(IfcMoq.ModelMock(), new FakeLoggerFactory(logger));

        int result = XbimGeometryNativeApi.xbim_curve_build_line_3d(
            service.ContextHandle,
            0, 0, 0,   // origin
            0, 0, 0,   // zero-length direction → gp_Dir throws Standard_ConstructionError
            out _);

        result.Should().NotBe(0, "native call must fail on zero direction");
        logger.Logs.Should().ContainSingle(l =>
            l.Level == LogLevel.Warning &&
            l.Message.Contains("xbim_curve_build_line_3d"));
    }

    /// <summary>
    /// gp_Ax2(center, normal, xDir) throws Standard_ConstructionError when
    /// normal and xDir are parallel.  Logged as Warning via xbim_log_occt_failure.
    /// </summary>
    [Fact]
    public void NativeOcctFailure_CollinearCircleAxes_LogsWarning()
    {
        var logger = new FakeLogger();
        using var service = new ModelGeometryService(IfcMoq.ModelMock(), new FakeLoggerFactory(logger));

        int result = XbimGeometryNativeApi.xbim_curve_build_circle_3d(
            service.ContextHandle,
            0, 0, 0,   // center
            1, 0, 0,   // normal  (X-axis)
            1, 0, 0,   // xDir    (X-axis) — parallel to normal → gp_Ax2 throws
            1.0,
            out _);

        result.Should().NotBe(0, "native call must fail on collinear axes");
        logger.Logs.Should().ContainSingle(l =>
            l.Level == LogLevel.Warning &&
            l.Message.Contains("xbim_curve_build_circle_3d"));
    }

    /// <summary>
    /// Same collinear-axes failure path for ellipse construction.
    /// </summary>
    [Fact]
    public void NativeOcctFailure_CollinearEllipseAxes_LogsWarning()
    {
        var logger = new FakeLogger();
        using var service = new ModelGeometryService(IfcMoq.ModelMock(), new FakeLoggerFactory(logger));

        int result = XbimGeometryNativeApi.xbim_curve_build_ellipse_3d(
            service.ContextHandle,
            0, 0, 0,   // center
            0, 0, 1,   // normal (Z-axis)
            0, 0, 1,   // xDir  (Z-axis) — parallel → gp_Ax2 throws
            2.0, 1.0,
            out _,
            out _);

        result.Should().NotBe(0, "native call must fail on collinear axes");
        logger.Logs.Should().ContainSingle(l =>
            l.Level == LogLevel.Warning &&
            l.Message.Contains("xbim_curve_build_ellipse_3d"));
    }

    #endregion

    /// <summary>
    /// Captures log calls made by the code under test.
    /// </summary>
    private sealed class FakeLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Logs { get; } = new();

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Logs.Add((logLevel, formatter(state, exception)));

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    /// <summary>
    /// Routes all category names to the same FakeLogger instance so that
    /// logs emitted through a ModelGeometryService can be captured.
    /// </summary>
    private sealed class FakeLoggerFactory : ILoggerFactory
    {
        private readonly FakeLogger _logger;
        public FakeLoggerFactory(FakeLogger logger) => _logger = logger;
        public ILogger CreateLogger(string categoryName) => _logger;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
    }
}
