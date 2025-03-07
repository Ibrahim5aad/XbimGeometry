#ifndef XBIM_GEOMETRY_NATIVE_H
#define XBIM_GEOMETRY_NATIVE_H

#include "xbim_geometry_api.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Returns the library version as a null-terminated string. */
XBIM_EXPORT const char* XBIM_CALL xbim_native_version(void);

/* Returns non-zero if the native library is operational. */
XBIM_EXPORT int XBIM_CALL xbim_native_is_available(void);

#ifdef __cplusplus
}
#endif

#endif /* XBIM_GEOMETRY_NATIVE_H */
