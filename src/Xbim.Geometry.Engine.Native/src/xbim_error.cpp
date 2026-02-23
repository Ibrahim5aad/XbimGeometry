/*
 * xbim_error.cpp
 *
 * Thread-local error message storage and retrieval.
 * Any native API function can call xbim_set_error() to record a message,
 * and managed code retrieves it via xbim_get_last_error().
 */

#include "xbim_geometry_api.h"
#include "xbim_error.h"

#include <string>

static thread_local std::string g_lastError;

void xbim_set_error(const char* msg)
{
    if (msg)
        g_lastError = msg;
    else
        g_lastError.clear();
}

void xbim_clear_error()
{
    g_lastError.clear();
}

XBIM_EXPORT const char* XBIM_CALL xbim_get_last_error(void)
{
    return g_lastError.c_str();
}
