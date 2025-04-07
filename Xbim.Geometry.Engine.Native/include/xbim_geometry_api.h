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

/* ── Sweep operations (extruded area solids) ─────────────────────────────── */

/*
 * Build an extruded area solid (linear sweep / prism).
 * Extrudes a face along a direction vector by the given depth.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   faceHandle       – a face shape handle (the profile to extrude)
 *   dirX/Y/Z         – extrusion direction (unit vector)
 *   depth            – extrusion distance (must be > 0)
 *   locationHandle   – optional location transform (may be NULL for identity)
 *   outHandle        – receives the new solid shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_extruded(
    XbimContextHandle   ctx,
    XbimShapeHandle     faceHandle,
    double dirX, double dirY, double dirZ,
    double depth,
    XbimLocationHandle  locationHandle,
    XbimShapeHandle*    outHandle);

/*
 * Build a tapered extruded area solid via ThruSections loft.
 * Lofts between a start profile face and an end profile face placed
 * at depth along the extrusion direction. Inner wires (voids) in
 * the profiles are handled by lofting each void pair and cutting
 * from the outer body.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   startFaceHandle  – start profile face handle
 *   endFaceHandle    – end profile face handle (may differ from start)
 *   dirX/Y/Z         – extrusion direction (unit vector)
 *   depth            – extrusion distance (must be > 0)
 *   precision        – surface generation tolerance (from model precision)
 *   locationHandle   – optional location transform (may be NULL for identity)
 *   outHandle        – receives the new shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_extruded_tapered(
    XbimContextHandle   ctx,
    XbimShapeHandle     startFaceHandle,
    XbimShapeHandle     endFaceHandle,
    double dirX, double dirY, double dirZ,
    double depth,
    double precision,
    XbimLocationHandle  locationHandle,
    XbimShapeHandle*    outHandle);

/* ── Sweep operations (revolved area solids) ──────────────────────────────── */

/*
 * Build a revolved area solid.
 * Revolves a face around an axis by the given angle using BRepPrimAPI_MakeRevol.
 *
 *   ctx                – a valid context handle (used for logging; may be NULL)
 *   faceHandle         – a face shape handle (the profile to revolve)
 *   axisOriginX/Y/Z    – revolution axis origin point
 *   axisDirX/Y/Z       – revolution axis direction (unit vector)
 *   angle              – revolution angle in radians (must be > 0)
 *   locationHandle     – optional location transform (may be NULL for identity)
 *   outHandle          – receives the new solid shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_revolved(
    XbimContextHandle   ctx,
    XbimShapeHandle     faceHandle,
    double axisOriginX, double axisOriginY, double axisOriginZ,
    double axisDirX,    double axisDirY,    double axisDirZ,
    double angle,
    XbimLocationHandle  locationHandle,
    XbimShapeHandle*    outHandle);

/*
 * Build a tapered revolved area solid via MakePipeShell along an arc.
 * Sweeps between start and end profile faces along a circular arc path
 * around the revolution axis. Supports hollow profiles with inner wires.
 *
 *   ctx                – a valid context handle (used for logging; may be NULL)
 *   faceHandle         – start profile face handle
 *   endFaceHandle      – end profile face handle (may differ from start)
 *   axisOriginX/Y/Z    – revolution axis origin point
 *   axisDirX/Y/Z       – revolution axis direction (unit vector)
 *   angle              – revolution angle in radians (must be > 0; clamped to 2*PI)
 *   precision          – surface generation tolerance (from model precision)
 *   locationHandle     – optional location transform (may be NULL for identity)
 *   outHandle          – receives the new shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_revolved_tapered(
    XbimContextHandle   ctx,
    XbimShapeHandle     faceHandle,
    XbimShapeHandle     endFaceHandle,
    double axisOriginX, double axisOriginY, double axisOriginZ,
    double axisDirX,    double axisDirY,    double axisDirZ,
    double angle,
    double precision,
    XbimLocationHandle  locationHandle,
    XbimShapeHandle*    outHandle);

/*
 * Build a swept disk solid by sweeping a circular cross-section along a wire directrix.
 * Ports NSolidFactory::BuildSweptDiskSolid from the C++/CLI engine.
 *
 * The directrix is a wire (shape handle of type Wire). A circle of the given
 * radius is swept along the wire using BRepOffsetAPI_MakePipeShell.
 * If innerRadius > 0, a hollow tube is created.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   directrixHandle  – a shape handle containing a TopoDS_Wire (the sweep path)
 *   radius           – outer radius of the circular cross-section (must be > 0)
 *   innerRadius      – inner radius for hollow tubes (use NaN or <= 0 for solid)
 *   outHandle        – receives the new solid shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_swept_disk(
    XbimContextHandle   ctx,
    XbimShapeHandle     directrixHandle,
    double              radius,
    double              innerRadius,
    XbimShapeHandle*    outHandle);

/*
 * Build a fixed reference swept area solid.
 * Sweeps a planar face along a wire directrix, maintaining orientation relative
 * to a reference surface. Ports NSolidFactory::BuildSurfaceCurveSweptAreaSolid.
 *
 * The reference surface is defined as a plane (origin + normal). The swept area
 * is repositioned to the start of the directrix with its normal tangent to the
 * sweep path and its X direction perpendicular to the reference surface.
 *
 *   ctx                       – a valid context handle (used for logging; may be NULL)
 *   faceHandle                – the planar swept area face to sweep
 *   directrixHandle           – a shape handle containing a TopoDS_Wire (the sweep path)
 *   refSurfaceOriginX/Y/Z    – reference surface plane origin point
 *   refSurfaceNormalX/Y/Z    – reference surface plane normal direction
 *   isPlanarReferenceSurface  – 1 if reference surface is planar, 0 otherwise
 *   precision                 – model precision tolerance (> 0)
 *   locationHandle            – optional location transform (may be NULL for identity)
 *   outHandle                 – receives the new solid shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_fixed_reference_swept(
    XbimContextHandle   ctx,
    XbimShapeHandle     faceHandle,
    XbimShapeHandle     directrixHandle,
    double refSurfaceOriginX, double refSurfaceOriginY, double refSurfaceOriginZ,
    double refSurfaceNormalX, double refSurfaceNormalY, double refSurfaceNormalZ,
    int    isPlanarReferenceSurface,
    double precision,
    XbimLocationHandle  locationHandle,
    XbimShapeHandle*    outHandle);

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

/*
 * Build a trapezium (trapezoid) profile face.
 * The profile is defined by a bottom edge, a top edge, and height.
 * TopXOffset shifts the top edge relative to the bottom left corner.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin
 *   zDirX/Y/Z        – placement Z direction (face normal)
 *   xDirX/Y/Z        – placement X direction (reference)
 *   bottomXDim        – width of the bottom edge (must be > 0)
 *   topXDim           – width of the top edge (must be > 0)
 *   yDim              – height of the trapezium (must be > 0)
 *   topXOffset        – horizontal offset of the top-left corner from the bottom-left
 *   outHandle         – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_trapezium(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double bottomXDim, double topXDim, double yDim, double topXOffset,
    XbimShapeHandle* outHandle);

/*
 * Build an asymmetric I-shape profile face.
 * This supports different top and bottom flange widths, thicknesses,
 * fillet radii, edge radii, and slope angles.
 *
 *   ctx                    – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z            – placement origin
 *   zDirX/Y/Z              – placement Z direction (face normal)
 *   xDirX/Y/Z              – placement X direction (reference)
 *   bottomFlangeWidth       – width of the bottom flange (must be > 0)
 *   overallDepth            – total section depth (must be > 0)
 *   webThickness            – thickness of the web (must be > 0)
 *   bottomFlangeThickness   – thickness of the bottom flange (must be > 0)
 *   topFlangeWidth          – width of the top flange (must be > 0)
 *   topFlangeThickness      – thickness of the top flange (0 = same as bottom)
 *   bottomFlangeFilletRadius – fillet at bottom web/flange junction (0 = no fillet)
 *   topFlangeFilletRadius   – fillet at top web/flange junction (0 = no fillet)
 *   bottomFlangeEdgeRadius  – edge radius on bottom flange (0 = no fillet)
 *   topFlangeEdgeRadius     – edge radius on top flange (0 = no fillet)
 *   bottomFlangeSlope       – slope angle on bottom flange in radians (0 = no slope)
 *   topFlangeSlope          – slope angle on top flange in radians (0 = no slope)
 *   outHandle               – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_asymmetric_ishape(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double bottomFlangeWidth, double overallDepth,
    double webThickness, double bottomFlangeThickness,
    double topFlangeWidth, double topFlangeThickness,
    double bottomFlangeFilletRadius, double topFlangeFilletRadius,
    double bottomFlangeEdgeRadius, double topFlangeEdgeRadius,
    double bottomFlangeSlope, double topFlangeSlope,
    XbimShapeHandle* outHandle);

/* ── Hollow profile primitives ────────────────────────────────────────── */

/*
 * Build a rectangle hollow profile face (rectangle with a rectangular hole).
 * The outer rectangle is centered at the placement origin in the XY plane.
 * The inner rectangle is inset by wallThickness on each side.
 * Optional inner/outer fillet radii apply rounded corners.
 *
 *   ctx                – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z        – placement origin
 *   zDirX/Y/Z          – placement Z direction (face normal)
 *   xDirX/Y/Z          – placement X direction (reference)
 *   xDim               – overall X dimension (must be > 0)
 *   yDim               – overall Y dimension (must be > 0)
 *   wallThickness      – wall thickness (must be > 0, < xDim/2 and < yDim/2)
 *   innerFilletRadius  – fillet at inner corners (0 = no fillet)
 *   outerFilletRadius  – fillet at outer corners (0 = no fillet)
 *   outHandle          – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_rectangle_hollow(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double xDim,    double yDim,    double wallThickness,
    double innerFilletRadius, double outerFilletRadius,
    XbimShapeHandle* outHandle);

/*
 * Build a circle hollow profile face (annular ring / tube cross-section).
 * The outer circle is centered at the placement origin in the XY plane.
 * The inner circle has radius = radius - wallThickness.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin
 *   zDirX/Y/Z        – placement Z direction (face normal)
 *   xDirX/Y/Z        – placement X direction (reference)
 *   radius            – outer radius (must be > 0)
 *   wallThickness     – wall thickness (must be > 0, < radius)
 *   outHandle         – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_circle_hollow(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,  double wallThickness,
    XbimShapeHandle* outHandle);

/* ── Arbitrary / composite / derived profile primitives ────────────────── */

/*
 * Build a closed face from an arbitrary 2D polyline.
 * The points define a closed polygon in the XY plane. The first and last
 * points are connected automatically. Winding is normalized to
 * counter-clockwise (face normal = +Z).
 *
 *   ctx          – a valid context handle (used for logging; may be NULL)
 *   pointsX      – array of X coordinates (must have pointCount elements)
 *   pointsY      – array of Y coordinates (must have pointCount elements)
 *   pointCount   – number of points (must be >= 3)
 *   outHandle    – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_arbitrary_closed(
    XbimContextHandle ctx,
    const double*     pointsX,
    const double*     pointsY,
    int               pointCount,
    XbimShapeHandle*  outHandle);

/*
 * Build an open wire from an arbitrary 2D polyline.
 * The points define an open polyline in the XY plane.
 * Adjacent points within tolerance are merged.
 *
 *   ctx          – a valid context handle (used for logging; may be NULL)
 *   pointsX      – array of X coordinates (must have pointCount elements)
 *   pointsY      – array of Y coordinates (must have pointCount elements)
 *   pointCount   – number of points (must be >= 2)
 *   outHandle    – receives the new wire shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_arbitrary_open(
    XbimContextHandle ctx,
    const double*     pointsX,
    const double*     pointsY,
    int               pointCount,
    XbimShapeHandle*  outHandle);

/*
 * Build a face with voids from an outer face and inner wire holes.
 * The outer face provides the outer boundary. Each inner wire defines
 * a hole to cut from the face. Inner wires are automatically reversed
 * to clockwise orientation. ShapeFix is applied to fix any winding issues.
 *
 *   ctx               – a valid context handle (used for logging; may be NULL)
 *   outerFaceHandle   – a face shape handle providing the outer boundary
 *   innerWireHandles  – array of shape handles for inner wire/face holes
 *   numInnerWires     – number of inner wire handles (must be >= 1)
 *   outHandle         – receives the new face shape handle with voids
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_with_voids(
    XbimContextHandle      ctx,
    XbimShapeHandle        outerFaceHandle,
    const XbimShapeHandle* innerWireHandles,
    int                    numInnerWires,
    XbimShapeHandle*       outHandle);

/*
 * Build a composite profile by combining multiple profile shapes into
 * a TopoDS_Compound. Each input profile is added as-is to the compound.
 *
 *   ctx             – a valid context handle (used for logging; may be NULL)
 *   profileHandles  – array of shape handles to combine
 *   numProfiles     – number of profiles (must be >= 1)
 *   outHandle       – receives the new compound shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_composite(
    XbimContextHandle      ctx,
    const XbimShapeHandle* profileHandles,
    int                    numProfiles,
    XbimShapeHandle*       outHandle);

/*
 * Build a derived profile by applying a 2D affine transform to a parent
 * shape. The transform is specified as a 2x3 matrix:
 *   [[m00, m01, m02],    (X' = m00*X + m01*Y + m02)
 *    [m10, m11, m12]]    (Y' = m10*X + m11*Y + m12)
 *
 * When isNonUniformScale is non-zero, gp_GTrsf (general transform) is
 * used to allow non-uniform X/Y scaling. Otherwise, gp_Trsf (rigid +
 * uniform scale) is used for better performance.
 *
 *   ctx                – a valid context handle (used for logging; may be NULL)
 *   parentHandle       – the parent profile shape to transform
 *   m00..m12           – 2x3 affine transform matrix coefficients
 *   isNonUniformScale  – non-zero to use general transform (supports non-uniform scaling)
 *   outHandle          – receives the new transformed shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_derived(
    XbimContextHandle ctx,
    XbimShapeHandle   parentHandle,
    double m00, double m01, double m02,
    double m10, double m11, double m12,
    int               isNonUniformScale,
    XbimShapeHandle*  outHandle);

/*
 * Build a mirrored profile by reflecting a parent shape about the Y axis.
 * This matches the IFC IIfcMirroredProfileDef semantics.
 * The result is reversed to maintain correct face normal orientation.
 *
 *   ctx           – a valid context handle (used for logging; may be NULL)
 *   parentHandle  – the parent profile shape to mirror
 *   outHandle     – receives the new mirrored shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_profile_build_mirrored(
    XbimContextHandle ctx,
    XbimShapeHandle   parentHandle,
    XbimShapeHandle*  outHandle);

/* ── Boolean operations ────────────────────────────────────────────────── */

/*
 * Perform a boolean union (fuse) of two shapes.
 * Combines the body and tool shapes into a single shape containing
 * the volume of both. Handles empty shapes gracefully.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   bodyHandle       – the first operand shape (must not be NULL)
 *   toolHandle       – the second operand shape (must not be NULL)
 *   fuzzyTolerance   – tolerance for the boolean operation (use model precision)
 *   outHasWarnings   – receives 1 if warnings were generated, 0 otherwise
 *   outHandle        – receives the resulting shape handle on success
 *
 * Returns XBIM_OK on success; XBIM_NULL_SHAPE if both inputs are empty
 * or the operation produces a null result.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_boolean_union(
    XbimContextHandle   ctx,
    XbimShapeHandle     bodyHandle,
    XbimShapeHandle     toolHandle,
    double              fuzzyTolerance,
    int*                outHasWarnings,
    XbimShapeHandle*    outHandle);

/*
 * Perform a boolean cut (difference) of two shapes.
 * Subtracts the tool shape from the body shape. If the body is empty,
 * an empty shape is returned. If the tool is empty, the body is returned.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   bodyHandle       – the shape to cut from (must not be NULL)
 *   toolHandle       – the shape to subtract (must not be NULL)
 *   fuzzyTolerance   – tolerance for the boolean operation (use model precision)
 *   outHasWarnings   – receives 1 if warnings were generated, 0 otherwise
 *   outHandle        – receives the resulting shape handle on success
 *
 * Returns XBIM_OK on success; XBIM_NULL_SHAPE if both inputs are empty
 * or the operation produces a null result.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_boolean_cut(
    XbimContextHandle   ctx,
    XbimShapeHandle     bodyHandle,
    XbimShapeHandle     toolHandle,
    double              fuzzyTolerance,
    int*                outHasWarnings,
    XbimShapeHandle*    outHandle);

/*
 * Perform a boolean intersection (common) of two shapes.
 * Returns the volume common to both shapes. If either shape is empty,
 * returns XBIM_NULL_SHAPE since there can be no intersection.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   bodyHandle       – the first operand shape (must not be NULL)
 *   toolHandle       – the second operand shape (must not be NULL)
 *   fuzzyTolerance   – tolerance for the boolean operation (use model precision)
 *   outHasWarnings   – receives 1 if warnings were generated, 0 otherwise
 *   outHandle        – receives the resulting shape handle on success
 *
 * Returns XBIM_OK on success; XBIM_NULL_SHAPE if either input is empty
 * or the operation produces a null result.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_boolean_intersect(
    XbimContextHandle   ctx,
    XbimShapeHandle     bodyHandle,
    XbimShapeHandle     toolHandle,
    double              fuzzyTolerance,
    int*                outHasWarnings,
    XbimShapeHandle*    outHandle);

/* ── Half-space operations ─────────────────────────────────────────────── */

/*
 * Surface type for half-space construction.
 */
typedef enum XbimSurfaceType
{
    XBIM_SURFACE_PLANE       = 0,
    XBIM_SURFACE_CYLINDRICAL = 1,
    XBIM_SURFACE_SPHERICAL   = 2
} XbimSurfaceType;

/*
 * Build a half-space solid from an elementary surface.
 * The half-space is an infinite (or very large) solid on one side of
 * the surface, determined by the agreement flag.
 *
 * For planar surfaces: the normal direction is the Z axis of the placement.
 *   agreementFlag = false → material is on the positive-normal side
 *   agreementFlag = true  → material is on the negative-normal side
 *
 * For cylindrical/spherical surfaces: the surface centre is always in the material.
 *   agreementFlag = false → material is inside the surface
 *   agreementFlag = true  → material is outside the surface
 *
 *   ctx              – a valid context handle (used for logging/precision; may be NULL)
 *   surfaceType      – one of XBIM_SURFACE_PLANE/CYLINDRICAL/SPHERICAL
 *   originX/Y/Z      – surface placement origin
 *   zDirX/Y/Z        – surface placement Z direction (normal for planes, axis for cylinders/spheres)
 *   xDirX/Y/Z        – surface placement X direction (reference)
 *   radius           – radius for cylindrical/spherical surfaces (ignored for plane)
 *   agreementFlag    – IFC agreement flag (0 = false, non-zero = true)
 *   oneMeter         – model unit conversion for "one meter" (used for point-in-material offset)
 *   precision        – model precision tolerance
 *   outHandle        – receives the new solid shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_halfspace_build(
    XbimContextHandle ctx,
    XbimSurfaceType   surfaceType,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    int    agreementFlag,
    double oneMeter,
    double precision,
    XbimShapeHandle*  outHandle);

/*
 * Build a polygonal bounded half-space solid.
 * First builds the basic half-space, then intersects it with a prism
 * created by extruding the polygonal boundary along the surface normal.
 *
 * The polygonal boundary is a 2D closed polygon in the XY plane of the
 * boundary position. It is extruded along Z by ±(100 * oneMeter) to create
 * a cutting prism, which is then intersected with the half-space.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   surfaceOriginX/Y/Z – base surface placement origin
 *   surfaceZDirX/Y/Z   – base surface placement Z direction (must be planar)
 *   surfaceXDirX/Y/Z   – base surface placement X direction
 *   agreementFlag    – IFC agreement flag (0 = false, non-zero = true)
 *   boundaryPointsX  – array of X coordinates for the boundary polygon
 *   boundaryPointsY  – array of Y coordinates for the boundary polygon
 *   boundaryPointCount – number of boundary points (must be >= 3)
 *   boundaryOriginX/Y/Z – position of the boundary coordinate system
 *   boundaryZDirX/Y/Z   – Z direction of the boundary coordinate system
 *   boundaryXDirX/Y/Z   – X direction of the boundary coordinate system
 *   oneMeter         – model unit conversion for "one meter"
 *   precision        – model precision tolerance
 *   outHandle        – receives the new solid shape handle
 *
 * Returns XBIM_OK on success; XBIM_NULL_SHAPE if the boundary is empty
 * or the intersection produces an empty result.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_halfspace_build_polygonal_bounded(
    XbimContextHandle ctx,
    double surfaceOriginX, double surfaceOriginY, double surfaceOriginZ,
    double surfaceZDirX,   double surfaceZDirY,   double surfaceZDirZ,
    double surfaceXDirX,   double surfaceXDirY,   double surfaceXDirZ,
    int    agreementFlag,
    const double* boundaryPointsX,
    const double* boundaryPointsY,
    int    boundaryPointCount,
    double boundaryOriginX, double boundaryOriginY, double boundaryOriginZ,
    double boundaryZDirX,   double boundaryZDirY,   double boundaryZDirZ,
    double boundaryXDirX,   double boundaryXDirY,   double boundaryXDirZ,
    double oneMeter,
    double precision,
    XbimShapeHandle*  outHandle);

/*
 * Build a boxed half-space solid.
 * Per IFC specification, the boxed half-space is semantically identical
 * to a basic half-space — the bounding box is only for computational
 * efficiency hints. This function delegates directly to xbim_halfspace_build.
 *
 * Parameters are identical to xbim_halfspace_build.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_halfspace_build_boxed(
    XbimContextHandle ctx,
    XbimSurfaceType   surfaceType,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    int    agreementFlag,
    double oneMeter,
    double precision,
    XbimShapeHandle*  outHandle);

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
