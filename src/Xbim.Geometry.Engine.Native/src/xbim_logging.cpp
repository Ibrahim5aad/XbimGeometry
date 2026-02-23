/*
 * xbim_logging.cpp
 *
 * Implements context-bound logging with printf-style formatting.
 * All log messages pass through the XbimLogCallback stored in the context.
 * 
 */

#include "xbim_logging.h"
#include "xbim_context.h"

#include <cstdarg>
#include <cstdio>
#include <sstream>
#include <Standard_Failure.hxx>

/* Maximum length for a single formatted log message */
static const int LOG_BUFFER_SIZE = 2048;

#pragma region Logging Internals

static void log_formatted_va(const XbimContext_* ctx, int level,
                             const char* fmt, va_list args)
{
    if (!ctx || !ctx->logCallback || !fmt)
        return;

    char buffer[LOG_BUFFER_SIZE];
    int written = vsnprintf(buffer, LOG_BUFFER_SIZE, fmt, args);

    /* Ensure null termination even if truncated */
    if (written < 0)
        buffer[0] = '\0';
    else if (written >= LOG_BUFFER_SIZE)
        buffer[LOG_BUFFER_SIZE - 1] = '\0';

    ctx->logCallback(level, buffer);
}


void xbim_log_message(const XbimContext_* ctx, int level,
                      const char* fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    log_formatted_va(ctx, level, fmt, args);
    va_end(args);
}

void xbim_log_debug(const XbimContext_* ctx, const char* fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    log_formatted_va(ctx, XBIM_LOG_DEBUG, fmt, args);
    va_end(args);
}

void xbim_log_info(const XbimContext_* ctx, const char* fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    log_formatted_va(ctx, XBIM_LOG_INFO, fmt, args);
    va_end(args);
}

void xbim_log_warning(const XbimContext_* ctx, const char* fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    log_formatted_va(ctx, XBIM_LOG_WARNING, fmt, args);
    va_end(args);
}

void xbim_log_error(const XbimContext_* ctx, const char* fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    log_formatted_va(ctx, XBIM_LOG_ERROR, fmt, args);
    va_end(args);
}

void xbim_log_critical(const XbimContext_* ctx, const char* fmt, ...)
{
    va_list args;
    va_start(args, fmt);
    log_formatted_va(ctx, XBIM_LOG_CRITICAL, fmt, args);
    va_end(args);
}


void xbim_log_occt_failure(const XbimContext_* ctx, const Standard_Failure& e,
                           const char* context_msg)
{
    if (!ctx || !ctx->logCallback)
        return;

    std::stringstream strm;
    if (context_msg)
        strm << context_msg << ": ";
    e.Print(strm);

    ctx->logCallback(XBIM_LOG_WARNING, strm.str().c_str());
}

#pragma endregion

#pragma region Logging Exports

XBIM_EXPORT XbimResult XBIM_CALL xbim_context_set_logger(
    XbimContextHandle handle,
    XbimLogCallback   logCallback)
{
    if (!handle)
        return XBIM_INVALID_HANDLE;

    handle->logCallback = logCallback;
    return XBIM_OK;
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_context_log(
    XbimContextHandle handle,
    int               level,
    const char*       message)
{
    if (!handle)
        return XBIM_INVALID_HANDLE;

    if (handle->logCallback && message)
        handle->logCallback(level, message);

    return XBIM_OK;
}

#pragma endregion
