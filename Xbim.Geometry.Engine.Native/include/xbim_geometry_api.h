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

/* ── Location handle lifecycle ────────────────────────────────────────────── */

/*
 * Create a location (transform) from an axis-2 placement.
 * The axis-2 placement is defined by an origin point, Z direction (normal),
 * and X direction (reference direction).
 *
 *   originX/Y/Z  – coordinates of the placement origin
 *   zDirX/Y/Z    – normal direction (must be a valid non-zero direction)
 *   xDirX/Y/Z    – reference direction (must be a valid non-zero direction)
 *   outHandle     – receives the new location handle on success
 *
 * Returns XBIM_OK on success; XBIM_INVALID_ARG if outHandle is NULL;
 * XBIM_ERROR on OCCT failure (e.g., zero-length direction).
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_location_create_from_axis2(
    double originX, double originY, double originZ,
    double zDirX, double zDirY, double zDirZ,
    double xDirX, double xDirY, double xDirZ,
    XbimLocationHandle* outHandle);

/*
 * Create an identity location (no transformation).
 *
 *   outHandle – receives the new location handle on success
 *
 * Returns XBIM_OK on success; XBIM_INVALID_ARG if outHandle is NULL.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_location_create_identity(
    XbimLocationHandle* outHandle);

/*
 * Compose two locations into a single combined transform: result = loc1 * loc2.
 * Neither input location is modified; a new handle is allocated.
 *
 *   loc1      – first location (applied second geometrically)
 *   loc2      – second location (applied first geometrically)
 *   outHandle – receives the composed location handle on success
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if loc1 or loc2 is NULL;
 * XBIM_INVALID_ARG if outHandle is NULL.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_location_compose(
    XbimLocationHandle  loc1,
    XbimLocationHandle  loc2,
    XbimLocationHandle* outHandle);

/*
 * Destroy a location handle and free its resources.
 * Passing NULL is a safe no-op.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_location_destroy(XbimLocationHandle handle);

/*
 * Apply a location transform to a shape, producing a new shape at the
 * transformed position. The original shape is not modified.
 *
 *   shapeHandle    – a valid shape handle (source shape)
 *   locationHandle – a valid location handle (transform to apply)
 *   outHandle      – receives the new transformed shape handle on success
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if shapeHandle or
 * locationHandle is NULL; XBIM_NULL_SHAPE if the source shape is null.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_moved(
    XbimShapeHandle     shapeHandle,
    XbimLocationHandle  locationHandle,
    XbimShapeHandle*    outHandle);

/* ── CSG solid primitives ────────────────────────────────────────────────── */

/*
 * Build a rectangular block (box) solid.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin
 *   zDirX/Y/Z        – placement Z direction (normal)
 *   xDirX/Y/Z        – placement X direction (reference)
 *   xLen, yLen, zLen  – box dimensions (must be > 0)
 *   outHandle         – receives the new solid shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_block(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double xLen,    double yLen,    double zLen,
    XbimShapeHandle* outHandle);

/*
 * Build a sphere solid.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin (center of the sphere)
 *   zDirX/Y/Z        – placement Z direction
 *   xDirX/Y/Z        – placement X direction
 *   radius            – sphere radius (must be > 0)
 *   outHandle         – receives the new solid shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_sphere(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    XbimShapeHandle* outHandle);

/*
 * Build a right circular cylinder solid.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin (center of base circle)
 *   zDirX/Y/Z        – placement Z direction (cylinder axis)
 *   xDirX/Y/Z        – placement X direction
 *   radius            – cylinder radius (must be > 0)
 *   height            – cylinder height (must be > 0)
 *   outHandle         – receives the new solid shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_right_circular_cylinder(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,  double height,
    XbimShapeHandle* outHandle);

/*
 * Build a right circular cone solid with apex at top.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin (center of base circle)
 *   zDirX/Y/Z        – placement Z direction (cone axis)
 *   xDirX/Y/Z        – placement X direction
 *   radius            – base circle radius (must be > 0)
 *   height            – cone height (must be > 0)
 *   outHandle         – receives the new solid shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_right_circular_cone(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,  double height,
    XbimShapeHandle* outHandle);

/*
 * Build a rectangular pyramid solid.
 * The pyramid has a rectangular base (xLen x yLen) and an apex at height
 * centered above the base.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin
 *   zDirX/Y/Z        – placement Z direction (pyramid axis)
 *   xDirX/Y/Z        – placement X direction
 *   xLen, yLen        – base rectangle dimensions (must be > 0)
 *   height            – pyramid height (must be > 0)
 *   outHandle         – receives the new solid shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_rectangular_pyramid(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double xLen,    double yLen,    double height,
    XbimShapeHandle* outHandle);

/* ── Parametric profile primitives ────────────────────────────────────────── */

/*
 * Build a rectangular profile face.
 * The rectangle is centered at the placement origin in the XY plane.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin
 *   zDirX/Y/Z        – placement Z direction (face normal)
 *   xDirX/Y/Z        – placement X direction (reference)
 *   xDim, yDim        – rectangle dimensions (must be > 0)
 *   outHandle         – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_rectangle(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double xDim,    double yDim,
    XbimShapeHandle* outHandle);

/*
 * Build a circular profile face.
 * The circle is centered at the placement origin in the XY plane.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin (center of the circle)
 *   zDirX/Y/Z        – placement Z direction (face normal)
 *   xDirX/Y/Z        – placement X direction (reference)
 *   radius            – circle radius (must be > 0)
 *   outHandle         – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_circle(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    XbimShapeHandle* outHandle);

/*
 * Build an elliptical profile face.
 * The ellipse is centered at the placement origin in the XY plane.
 * semiAxis1 is along the X direction, semiAxis2 along the Y direction.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin (center of the ellipse)
 *   zDirX/Y/Z        – placement Z direction (face normal)
 *   xDirX/Y/Z        – placement X direction (reference)
 *   semiAxis1         – semi-axis along X (must be > 0)
 *   semiAxis2         – semi-axis along Y (must be > 0)
 *   outHandle         – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_ellipse(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double semiAxis1, double semiAxis2,
    XbimShapeHandle* outHandle);

/*
 * Build a rounded rectangle profile face.
 * The rectangle is centered at the placement origin in the XY plane,
 * with fillets applied at each corner.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin
 *   zDirX/Y/Z        – placement Z direction (face normal)
 *   xDirX/Y/Z        – placement X direction (reference)
 *   xDim, yDim        – rectangle dimensions (must be > 0)
 *   roundingRadius    – corner rounding radius (must be >= 0; 0 = no rounding)
 *   outHandle         – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_rounded_rectangle(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double xDim,    double yDim,    double roundingRadius,
    XbimShapeHandle* outHandle);

/* ── Structural profile primitives ───────────────────────────────────────── */

/*
 * Build a symmetric I-shape (wide-flange) profile face.
 * The profile is centered at the placement origin in the XY plane.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin
 *   zDirX/Y/Z        – placement Z direction (face normal)
 *   xDirX/Y/Z        – placement X direction (reference)
 *   overallWidth      – total flange width (must be > 0)
 *   overallDepth      – total section depth (must be > 0)
 *   webThickness      – thickness of the web (must be > 0)
 *   flangeThickness   – thickness of each flange (must be > 0)
 *   filletRadius      – fillet radius at web/flange junction (0 = no fillet)
 *   outHandle         – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_ishape(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double overallWidth, double overallDepth,
    double webThickness, double flangeThickness,
    double filletRadius,
    XbimShapeHandle* outHandle);

/*
 * Build an L-shape (angle) profile face.
 * The profile is centered at the placement origin in the XY plane.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin
 *   zDirX/Y/Z        – placement Z direction (face normal)
 *   xDirX/Y/Z        – placement X direction (reference)
 *   depth             – vertical leg length (must be > 0)
 *   width             – horizontal leg length (0 = same as depth)
 *   thickness         – leg thickness (must be > 0)
 *   filletRadius      – fillet at the internal corner (0 = no fillet)
 *   edgeRadius        – fillet at the outer leg corners (0 = no fillet)
 *   legSlope          – slope angle of the legs in radians (0 = no slope)
 *   outHandle         – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_lshape(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double depth, double width, double thickness,
    double filletRadius, double edgeRadius, double legSlope,
    XbimShapeHandle* outHandle);

/*
 * Build a T-shape profile face.
 * The profile is centered at the placement origin in the XY plane.
 *
 *   ctx               – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z       – placement origin
 *   zDirX/Y/Z         – placement Z direction (face normal)
 *   xDirX/Y/Z         – placement X direction (reference)
 *   depth              – overall section depth (must be > 0)
 *   flangeWidth        – width of the top flange (must be > 0)
 *   webThickness       – thickness of the web (must be > 0)
 *   flangeThickness    – thickness of the flange (must be > 0)
 *   filletRadius       – fillet at web/flange junction (0 = no fillet)
 *   flangeEdgeRadius   – fillet at outer flange corners (0 = no fillet)
 *   webEdgeRadius      – fillet at web bottom corners (0 = no fillet)
 *   flangeSlope        – slope angle on flange in radians (0 = no slope)
 *   webSlope           – slope angle on web in radians (0 = no slope)
 *   outHandle          – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_tshape(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double depth, double flangeWidth,
    double webThickness, double flangeThickness,
    double filletRadius, double flangeEdgeRadius, double webEdgeRadius,
    double flangeSlope, double webSlope,
    XbimShapeHandle* outHandle);

/*
 * Build a U-shape (channel) profile face.
 * The profile is centered at the placement origin in the XY plane.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin
 *   zDirX/Y/Z        – placement Z direction (face normal)
 *   xDirX/Y/Z        – placement X direction (reference)
 *   depth             – overall section depth (must be > 0)
 *   flangeWidth       – width of the flanges (must be > 0)
 *   webThickness      – thickness of the web (must be > 0)
 *   flangeThickness   – thickness of the flanges (must be > 0)
 *   filletRadius      – fillet at web/flange junction (0 = no fillet)
 *   edgeRadius        – fillet at outer flange corners (0 = no fillet)
 *   flangeSlope       – slope angle on flanges in radians (0 = no slope)
 *   outHandle         – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_ushape(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double depth, double flangeWidth,
    double webThickness, double flangeThickness,
    double filletRadius, double edgeRadius, double flangeSlope,
    XbimShapeHandle* outHandle);

/*
 * Build a Z-shape profile face.
 * The profile is centered at the placement origin in the XY plane.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin
 *   zDirX/Y/Z        – placement Z direction (face normal)
 *   xDirX/Y/Z        – placement X direction (reference)
 *   depth             – overall section depth (must be > 0)
 *   flangeWidth       – width of each flange (must be > 0)
 *   webThickness      – thickness of the web (must be > 0)
 *   flangeThickness   – thickness of flanges (must be > 0)
 *   filletRadius      – fillet at web/flange junction (0 = no fillet)
 *   edgeRadius        – fillet at outer web corners (0 = no fillet)
 *   outHandle         – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_zshape(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double depth, double flangeWidth,
    double webThickness, double flangeThickness,
    double filletRadius, double edgeRadius,
    XbimShapeHandle* outHandle);

/*
 * Build a C-shape profile face.
 * The profile is centered at the placement origin in the XY plane.
 * The girth parameter controls the horizontal return at the top/bottom
 * of the channel opening. When girth <= 0, a simplified 8-vertex C-shape
 * is built without returns.
 *
 *   ctx                  – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z          – placement origin
 *   zDirX/Y/Z            – placement Z direction (face normal)
 *   xDirX/Y/Z            – placement X direction (reference)
 *   depth                 – overall section depth (must be > 0)
 *   width                 – overall section width (must be > 0)
 *   wallThickness         – wall thickness (must be > 0)
 *   girth                 – girth/return dimension (0 = no return)
 *   internalFilletRadius  – fillet at internal corners (0 = no fillet)
 *   outHandle             – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_cshape(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double depth, double width, double wallThickness,
    double girth, double internalFilletRadius,
    XbimShapeHandle* outHandle);

/* ── BRep serialization ────────────────────────────────────────────────── */

/*
 * Write a shape to a file in OCCT BRep ASCII format.
 * Useful for debugging and test validation — the resulting .brep file
 * can be opened in CAD Assistant, FreeCAD, or read back via BRepTools::Read.
 *
 *   handle   – a valid shape handle
 *   filePath – null-terminated path to the output file (UTF-8 / ASCII)
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if handle is NULL;
 * XBIM_NULL_SHAPE if the shape is null; XBIM_ERROR on I/O or OCCT failure.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_write_brep(
    XbimShapeHandle handle,
    const char*     filePath);

#ifdef __cplusplus
}
#endif

#endif /* XBIM_GEOMETRY_API_H */
