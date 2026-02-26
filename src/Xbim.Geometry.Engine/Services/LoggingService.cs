using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xbim.Geometry.Abstractions;
using Xbim.Geometry.Engine.Handles;
using Xbim.Geometry.Engine.Internal;

namespace Xbim.Geometry.Engine.Services
{
    /// <summary>
    /// Managed logging service that bridges ILogger to the native XbimLogCallback.
    /// Keeps the delegate alive (preventing GC) while native code may invoke it.
    /// </summary>
    internal sealed class LoggingService : IXLoggingService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly XbimLogCallback _logCallback;
        private GCHandle _callbackHandle;
        private bool _disposed;

        public LoggingService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logCallback = OnNativeLog;
            _callbackHandle = GCHandle.Alloc(_logCallback);
        }

        public ILogger Logger => _logger;

        public IntPtr LogDelegatePtr =>
            Marshal.GetFunctionPointerForDelegate(_logCallback);

        /// <summary>
        /// The managed callback delegate, kept alive by <see cref="_callbackHandle"/>.
        /// Pass this to native context creation or set_logger calls.
        /// </summary>
        internal XbimLogCallback Callback => _logCallback;

        public void LogCritical(string logMsg) => _logger.LogCritical("{Message}", logMsg);
        public void LogError(string logMsg) => _logger.LogError("{Message}", logMsg);
        public void LogWarning(string logMsg) => _logger.LogWarning("{Message}", logMsg);
        public void LogInformation(string logMsg) => _logger.LogInformation("{Message}", logMsg);
        public void LogDebug(string logMsg) => _logger.LogDebug("{Message}", logMsg);

        /// <summary>
        /// Called by native code via the XbimLogCallback function pointer.
        /// Maps native log levels (1=Debug..5=Critical) to Microsoft.Extensions.Logging levels.
        /// </summary>
        private void OnNativeLog(int level, string message)
        {
            switch (level)
            {
                case 5:
                    _logger.LogCritical("{Message}", message);
                    break;
                case 4:
                    _logger.LogError("{Message}", message);
                    break;
                case 3:
                    _logger.LogWarning("{Message}", message);
                    break;
                case 2:
                    _logger.LogInformation("{Message}", message);
                    break;
                case 1:
                default:
                    _logger.LogDebug("{Message}", message);
                    break;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_callbackHandle.IsAllocated)
                _callbackHandle.Free();
        }
    }
}
