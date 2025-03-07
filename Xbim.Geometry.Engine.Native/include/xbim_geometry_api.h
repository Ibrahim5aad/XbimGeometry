/*
 * xbim_geometry_api.h
 *
 * Public C API for Xbim.Geometry.Engine.Native.
 * Defines opaque handle types, error handling, calling conventions,
 * and shared enumerations used across all native API functions.
 *
 * This header is valid in both C and C++ translation units.
 */

#ifndef XBIM_GEOMETRY_API_H
#define XBIM_GEOMETRY_API_H

#ifdef __cplusplus
extern "C" {
#endif

/* ── Export / calling-convention macros ─────────────────────────────────────── */

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

/* ── Opaque handle types ───────────────────────────────────────────────────── */

typedef struct XbimContext_*        XbimContextHandle;
typedef struct XbimShape_*          XbimShapeHandle;
typedef struct XbimLocation_*       XbimLocationHandle;
typedef struct XbimCurve_*          XbimCurveHandle;
typedef struct XbimSurface_*        XbimSurfaceHandle;

/* ── Result / error codes ──────────────────────────────────────────────────── */

typedef int XbimResult;

#define XBIM_OK              0  /* Success                                   */
#define XBIM_ERROR           1  /* General / unspecified error                */
#define XBIM_INVALID_HANDLE  2  /* A NULL or otherwise invalid handle was passed */
#define XBIM_NULL_SHAPE      3  /* The resulting shape is null / empty        */
#define XBIM_INVALID_ARG     4  /* An argument value is out of range          */

/*
 * Retrieve a human-readable description of the last error that occurred
 * on the calling thread. Returns an empty string when no error is pending.
 * The returned pointer remains valid until the next xbim_* call on the
 * same thread.
 */
XBIM_EXPORT const char* XBIM_CALL xbim_get_last_error(void);

/* ── Logging callback ──────────────────────────────────────────────────────── */

/*
 * Log levels (matching Xbim.Geometry.Engine NLoggingService convention).
 */
#define XBIM_LOG_DEBUG     1
#define XBIM_LOG_INFO      2
#define XBIM_LOG_WARNING   3
#define XBIM_LOG_ERROR     4
#define XBIM_LOG_CRITICAL  5

/*
 * Signature for a logging callback supplied by managed code.
 * The native library invokes this to forward log messages.
 *
 *   level – one of the XBIM_LOG_* constants above
 *   msg   – null-terminated UTF-8 message; valid only for the duration of the call
 */
typedef void (XBIM_CALL *XbimLogCallback)(int level, const char* msg);

/* ── Shape type enumeration ────────────────────────────────────────────────── */

/*
 * Matches Xbim.Geometry.Abstractions.XShapeType (C# enum).
 * Values are identical to TopAbs_ShapeEnum for Vertex..Compound.
 */
typedef enum XbimShapeType
{
    XBIM_SHAPE_VERTEX   = 0,
    XBIM_SHAPE_EDGE     = 1,
    XBIM_SHAPE_WIRE     = 2,
    XBIM_SHAPE_FACE     = 3,
    XBIM_SHAPE_SHELL    = 4,
    XBIM_SHAPE_SOLID    = 5,
    XBIM_SHAPE_COMPOUND = 6
} XbimShapeType;

#ifdef __cplusplus
}
#endif

#endif /* XBIM_GEOMETRY_API_H */
