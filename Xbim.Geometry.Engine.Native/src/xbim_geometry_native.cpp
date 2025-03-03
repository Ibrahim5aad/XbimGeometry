#include "xbim_geometry_native.h"

static const char* XBIM_NATIVE_VERSION = "0.1.0";

XBIM_EXPORT const char* XBIM_CALL xbim_native_version(void)
{
    return XBIM_NATIVE_VERSION;
}

XBIM_EXPORT int XBIM_CALL xbim_native_is_available(void)
{
    return 1;
}
