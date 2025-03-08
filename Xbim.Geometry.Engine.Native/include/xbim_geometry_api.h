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

/* ── Context lifecycle ─────────────────────────────────────────────────────── */

/*
 * Create a geometry context wrapping model-level parameters.
 * All geometry operations require a valid context handle.
 *
 *   precision      – model precision tolerance (must be > 0)
 *   oneMeter       – length of one meter in model units (must be > 0)
 *   oneFoot        – length of one foot in model units
 *   oneMillimeter  – length of one millimetre in model units
 *   radianFactor   – angle-to-radians conversion factor
 *   timeout        – operation timeout in seconds (0 = no timeout)
 *   minimumGap     – minimum gap distance in model units
 *   logCallback    – optional logging callback (may be NULL)
 *   outHandle      – receives the new context handle on success
 *
 * Returns XBIM_OK on success; XBIM_INVALID_ARG if parameters are invalid.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_context_create(
    double              precision,
    double              oneMeter,
    double              oneFoot,
    double              oneMillimeter,
    double              radianFactor,
    double              timeout,
    double              minimumGap,
    XbimLogCallback     logCallback,
    XbimContextHandle*  outHandle);

/*
 * Destroy a context handle and free its resources.
 * Passing NULL is a safe no-op.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_context_destroy(XbimContextHandle handle);

/* ── Context logging ──────────────────────────────────────────────────────── */

/*
 * Replace the logging callback on an existing context.
 * Pass NULL to disable logging. This matches the NLoggingService::SetLogger
 * pattern from the original C++/CLI engine.
 *
 * Returns XBIM_INVALID_HANDLE if handle is NULL.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_context_set_logger(
    XbimContextHandle  handle,
    XbimLogCallback    logCallback);

/*
 * Send a log message through the context's callback.
 * No-op if the callback is not set or message is NULL.
 * This allows managed code to verify the callback round-trip.
 *
 *   handle  – a valid context handle
 *   level   – one of the XBIM_LOG_* constants
 *   message – null-terminated UTF-8 string (valid for the call duration)
 *
 * Returns XBIM_INVALID_HANDLE if handle is NULL; XBIM_OK otherwise.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_context_log(
    XbimContextHandle  handle,
    int                level,
    const char*        message);

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

/* ── Shape handle lifecycle ───────────────────────────────────────────────── */

/*
 * Destroy a shape handle and free its resources.
 * Passing NULL is a safe no-op.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_destroy(XbimShapeHandle handle);

/*
 * Get the topological type of a shape.
 *
 *   handle  – a valid shape handle
 *   outType – receives the shape type on success
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if handle is NULL;
 * XBIM_NULL_SHAPE if the underlying shape is null.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_type(
    XbimShapeHandle handle,
    XbimShapeType*  outType);

/*
 * Check whether a shape is valid (non-null and passes BRepCheck).
 * Returns 1 if valid, 0 otherwise. NULL handles return 0.
 */
XBIM_EXPORT int XBIM_CALL xbim_shape_is_valid(XbimShapeHandle handle);

/*
 * Check whether a shape is topologically closed.
 * Returns 1 if closed, 0 otherwise. NULL handles return 0.
 */
XBIM_EXPORT int XBIM_CALL xbim_shape_is_closed(XbimShapeHandle handle);

/*
 * Compute the axis-aligned bounding box of a shape.
 *
 *   handle       – a valid shape handle
 *   minX..maxZ   – output pointers for the bounding box corners
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if handle is NULL;
 * XBIM_NULL_SHAPE if the shape is null; XBIM_ERROR on OCCT failure.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_bounding_box(
    XbimShapeHandle handle,
    double* minX, double* minY, double* minZ,
    double* maxX, double* maxY, double* maxZ);

/*
 * Compute the volume of a shape using GProp_GProps.
 * Meaningful for solids and closed shells.
 *
 *   handle    – a valid shape handle
 *   outVolume – receives the volume on success
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_volume(
    XbimShapeHandle handle,
    double*         outVolume);

/*
 * Compute the surface area of a shape using GProp_GProps.
 * Meaningful for faces, shells, and solids.
 *
 *   handle  – a valid shape handle
 *   outArea – receives the surface area on success
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_surface_area(
    XbimShapeHandle handle,
    double*         outArea);

#ifdef __cplusplus
}
#endif

#endif /* XBIM_GEOMETRY_API_H */
