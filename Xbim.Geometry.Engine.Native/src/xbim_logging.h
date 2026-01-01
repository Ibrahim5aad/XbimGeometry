/*
 * xbim_logging.h
 *
 * Internal logging helpers for the native geometry library.
 * Context-bound logging through the callback supplied by managed code.
 *
 */

#ifndef XBIM_LOGGING_H
#define XBIM_LOGGING_H

#include "xbim_geometry_api.h"

/* Forward-declare the context struct for internal use */
struct XbimContext_;

/*
 * Send a formatted log message through the context's callback.
 * Works like printf: accepts a format string and variable arguments.
 * No-op if the context is NULL or the callback is not set.
 *
 *   ctx   - the geometry context (may be NULL)
 *   level - one of XBIM_LOG_DEBUG .. XBIM_LOG_CRITICAL
 *   fmt   - printf-style format string
 *   ...   - format arguments
 */
void xbim_log_message(const XbimContext_* ctx, int level, const char* fmt, ...);

/*
 * Convenience helpers matching the existing NLoggingService API.
 * Each forwards to xbim_log_message with the appropriate level.
 */
void xbim_log_debug(const XbimContext_* ctx, const char* fmt, ...);
void xbim_log_info(const XbimContext_* ctx, const char* fmt, ...);
void xbim_log_warning(const XbimContext_* ctx, const char* fmt, ...);
void xbim_log_error(const XbimContext_* ctx, const char* fmt, ...);
void xbim_log_critical(const XbimContext_* ctx, const char* fmt, ...);

/*
 * Log an OCCT Standard_Failure exception with an optional context message.
 * Mirrors the LogStandardFailure pattern used in NFactoryBase / CanLogBase.
 */
#ifdef __cplusplus
class Standard_Failure;
void xbim_log_occt_failure(const XbimContext_* ctx, const Standard_Failure& e,
                           const char* context_msg = nullptr);
#endif

#endif /* XBIM_LOGGING_H */
