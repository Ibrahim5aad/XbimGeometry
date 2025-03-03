#ifndef XBIM_GEOMETRY_NATIVE_H
#define XBIM_GEOMETRY_NATIVE_H

#ifdef __cplusplus
extern "C" {
#endif

/* Export/import macro */
#if defined(_WIN32) || defined(_WIN64)
    #ifdef XBIM_BUILD_DLL
        #define XBIM_EXPORT __declspec(dllexport)
    #else
        #define XBIM_EXPORT __declspec(dllimport)
    #endif
    #define XBIM_CALL __stdcall
#else
    #define XBIM_EXPORT __attribute__((visibility("default")))
    #define XBIM_CALL
#endif

/* Returns the library version as a null-terminated string. */
XBIM_EXPORT const char* XBIM_CALL xbim_native_version(void);

/* Returns non-zero if the native library is operational. */
XBIM_EXPORT int XBIM_CALL xbim_native_is_available(void);

#ifdef __cplusplus
}
#endif

#endif /* XBIM_GEOMETRY_NATIVE_H */
