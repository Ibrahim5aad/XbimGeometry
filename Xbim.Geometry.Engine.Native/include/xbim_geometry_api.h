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

#pragma region Export And Calling Convention Macros

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

#pragma endregion

#pragma region Opaque Handle Types

typedef struct XbimContext_*        XbimContextHandle;
typedef struct XbimShape_*          XbimShapeHandle;
typedef struct XbimLocation_*       XbimLocationHandle;
typedef struct XbimCurve_*          XbimCurveHandle;
typedef struct XbimCurve2d_*        XbimCurve2dHandle;
typedef struct XbimSurface_*        XbimSurfaceHandle;
typedef struct XbimAdvancedBrepBuilder_* XbimAdvancedBrepBuilderHandle;

#pragma endregion

#pragma region Result And Error Codes

typedef int XbimResult;

#define XBIM_OK                0  /* Success                                        */
#define XBIM_ERROR             1  /* General / unspecified error                    */
#define XBIM_INVALID_HANDLE    2  /* A NULL or otherwise invalid handle was passed  */
#define XBIM_NULL_SHAPE        3  /* The resulting shape is null / empty            */
#define XBIM_INVALID_ARG       4  /* An argument value is out of range              */

#define XBIM_TRUE              1  /* Boolean true                                   */
#define XBIM_FALSE             0  /* Boolean false                                  */

/*
 * Retrieve a human-readable description of the last error that occurred
 * on the calling thread. Returns an empty string when no error is pending.
 * The returned pointer remains valid until the next xbim_* call on the
 * same thread.
 */
XBIM_EXPORT const char* XBIM_CALL xbim_get_last_error(void);

#pragma endregion

#pragma region Logging Callback

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

#pragma endregion

#pragma region Context Lifecycle

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

#pragma endregion

#pragma region Context Logging

/*
 * Replace the logging callback on an existing context.
 * Pass NULL to disable logging.
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

#pragma endregion

#pragma region Shape Type Enumeration

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

#pragma endregion

#pragma region Shape Handle Lifecycle

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
 * Check whether a shape is null (empty). A null handle or a handle wrapping
 * a default-constructed TopoDS_Shape returns 1. Returns 0 for non-null shapes.
 */
XBIM_EXPORT int XBIM_CALL xbim_shape_is_null(XbimShapeHandle handle);

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
 * Create a new shape handle with reversed orientation.
 * The underlying geometry is shared; only the orientation flag is flipped.
 *
 *   handle    – the source shape handle
 *   outHandle – receives a new handle wrapping the reversed shape
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_reversed(
    XbimShapeHandle  handle,
    XbimShapeHandle* outHandle);

/*
 * Test whether two shape handles refer to the same underlying shape
 * (same TShape pointer and location).
 *
 *   a, b      – the two shape handles to compare
 *   outSame   – receives 1 if the shapes are the same, 0 otherwise
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_is_same(
    XbimShapeHandle a,
    XbimShapeHandle b,
    int*            outSame);

/*
 * Compute a hash code for a shape suitable for use in hash tables.
 *
 *   handle   – the shape handle
 *   outHash  – receives the hash code
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_hash_code(
    XbimShapeHandle handle,
    int*            outHash);

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

#pragma endregion

#pragma region Location Handle Lifecycle

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
 *   loc1      – first location 
 *   loc2      – second location 
 *   outHandle – 
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if loc1 or loc2 is NULL;
 * XBIM_INVALID_ARG if outHandle is NULL.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_location_multiplied(
    XbimLocationHandle  loc1,
    XbimLocationHandle  loc2,
    XbimLocationHandle* outHandle);

/*
 * Destroy a location handle and free its resources.
 * Passing NULL is a safe no-op.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_location_destroy(XbimLocationHandle handle);

/*
 * Extract the 3x3 rotation matrix, translation, and scale from a location.
 * Matrix values follow the IXMatrix convention (rows = local axis directions):
 *   M11=Value(1,1), M12=Value(2,1), M13=Value(3,1)   // X axis in global
 *   M21=Value(1,2), M22=Value(2,2), M23=Value(3,2)   // Y axis in global
 *   M31=Value(1,3), M32=Value(2,3), M33=Value(3,3)   // Z axis in global
 * All output pointers are optional (NULL = skip).
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if handle is NULL.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_location_get_transform(
    XbimLocationHandle handle,
    double* outM11, double* outM12, double* outM13,
    double* outM21, double* outM22, double* outM23,
    double* outM31, double* outM32, double* outM33,
    double* outOffsetX, double* outOffsetY, double* outOffsetZ,
    double* outScale);

/*
 * Create the inverse of a location transform.
 * The original handle is not modified; a new handle is allocated.
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if handle is NULL;
 * XBIM_INVALID_ARG if outHandle is NULL.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_location_invert(
    XbimLocationHandle  handle,
    XbimLocationHandle* outHandle);

/*
 * Create a copy of a location with a replaced translation component.
 * The rotation is preserved; only the translation changes.
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if handle is NULL;
 * XBIM_INVALID_ARG if outHandle is NULL.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_location_translated(
    XbimLocationHandle  handle,
    double tx, double ty, double tz,
    XbimLocationHandle* outHandle);

/*
 * Create a copy of a location with a replaced scale factor.
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if handle is NULL;
 * XBIM_INVALID_ARG if outHandle is NULL.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_location_scaled(
    XbimLocationHandle  handle,
    double scaleFactor,
    XbimLocationHandle* outHandle);

/*
 * Extract the location (transform) from a shape.
 * Returns the location handle and the 3x3 rotation matrix, translation
 * vector, and scale factor. Matrix components follow gp_Trsf::Value(row, col)
 * convention (1-based): M11=Value(1,1), M12=Value(1,2), etc.
 *
 *   shapeHandle    – a valid shape handle
 *   outHandle      – receives the location handle
 *   outM11..outM33 – rotation matrix components (may be NULL)
 *   outOffsetX/Y/Z – translation components (may be NULL)
 *   outScale       – scale factor (may be NULL)
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if shapeHandle is NULL;
 * XBIM_NULL_SHAPE if the shape is null.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_get_location(
    XbimShapeHandle     shapeHandle,
    XbimLocationHandle* outHandle,
    double* outM11, double* outM12, double* outM13,
    double* outM21, double* outM22, double* outM23,
    double* outM31, double* outM32, double* outM33,
    double* outOffsetX, double* outOffsetY, double* outOffsetZ,
    double* outScale);

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

/*
 * Apply a general affine transformation (gp_GTrsf) to a shape, producing a
 * new transformed shape. Supports non-uniform scaling via ScaleX/Y/Z.
 *
 * The 3x4 matrix (m11..m33 + offsets) encodes rotation/reflection and
 * translation. If scaleX, scaleY, and scaleZ are all non-zero, a separate
 * scale transform is multiplied in.
 *
 *   shapeHandle                 – source shape
 *   m11..m33, offsetX/Y/Z      – 3x4 affine matrix
 *   scaleX, scaleY, scaleZ     – non-uniform scale factors (0 = no scale)
 *   outHandle                  – receives the new transformed shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_gtransform(
    XbimShapeHandle shapeHandle,
    double m11, double m12, double m13, double offsetX,
    double m21, double m22, double m23, double offsetY,
    double m31, double m32, double m33, double offsetZ,
    double scaleX, double scaleY, double scaleZ,
    XbimShapeHandle* outHandle);

#pragma endregion

#pragma region CSG Solid Primitives

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

#pragma endregion

#pragma region Extruded Area Solids

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

#pragma endregion

#pragma region Revolved Area Solids

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

/*
 * Build a sectioned spine solid.
 * Sweeps a series of pre-positioned cross-section faces along a spine wire
 * using BRepOffsetAPI_MakePipeShell. Each section's outer wire is added to
 * the pipe shell; inner wires (voids) are swept separately and cut from
 * the outer body. The sections must already be moved to their final positions
 * (caller handles IIfcAxis2Placement3D transforms).
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   spineHandle      – a shape handle containing a TopoDS_Wire (the spine curve)
 *   sectionHandles   – array of shape handles, each containing a positioned TopoDS_Face
 *   numSections      – number of elements in sectionHandles (must be >= 2)
 *   precision        – model precision tolerance (> 0)
 *   outHandle        – receives the new solid shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_sectioned_spine(
    XbimContextHandle        ctx,
    XbimShapeHandle          spineHandle,
    const XbimShapeHandle*   sectionHandles,
    int                      numSections,
    double                   precision,
    XbimShapeHandle*         outHandle);

/*
 * Build a surface-curve swept area solid.
 * Sweeps a planar profile face along a directrix wire that lies on an
 * arbitrary reference surface. The profile is repositioned to the start
 * of the directrix using the surface normal, then swept with
 * BRepOffsetAPI_MakePipeShell in surface-reference mode.
 *
 *   ctx                       – a valid context handle (used for logging; may be NULL)
 *   faceHandle                – swept area profile (must be a planar TopoDS_Face)
 *   directrixHandle           – directrix wire (TopoDS_Wire)
 *   surfaceHandle             – reference surface (Geom_Surface, may be non-planar)
 *   isPlanarReferenceSurface  – non-zero if the reference surface is a plane
 *   precision                 – model precision tolerance (> 0)
 *   locationHandle            – optional location transform (may be NULL for identity)
 *   outHandle                 – receives the new solid shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_solid_build_surface_curve_swept(
    XbimContextHandle   ctx,
    XbimShapeHandle     faceHandle,
    XbimShapeHandle     directrixHandle,
    XbimSurfaceHandle   surfaceHandle,
    int                 isPlanarReferenceSurface,
    double              precision,
    XbimLocationHandle  locationHandle,
    XbimShapeHandle*    outHandle);

#pragma endregion

#pragma region Parametric Profiles

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

#pragma endregion

#pragma region Structural Profiles

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

#pragma endregion

#pragma region Hollow Profiles

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

#pragma endregion

#pragma region Arbitrary And Derived Profiles

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

#pragma endregion

#pragma region Boolean Operations

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

/*
 * Perform a boolean section of a solid with a face.
 * Computes the intersection curves, assembles them into closed wires,
 * and reconstructs faces on the section surface.
 *
 *   ctx          – a valid context handle (used for logging; may be NULL)
 *   bodyHandle   – the solid to section (must not be NULL)
 *   faceHandle   – the face to section with (must not be NULL)
 *   tolerance    – geometric tolerance for edge/wire assembly
 *   outHandle    – receives a compound of faces on success (may be empty)
 *
 * Returns XBIM_OK on success. The result compound may contain zero faces
 * if the section plane does not intersect the solid interior.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_boolean_section(
    XbimContextHandle   ctx,
    XbimShapeHandle     bodyHandle,
    XbimShapeHandle     faceHandle,
    double              tolerance,
    XbimShapeHandle*    outHandle);

#pragma endregion

#pragma region Half-Space Operations

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

#pragma endregion

#pragma region Compound Operations

/*
 * Create a compound shape from an array of child shapes.
 * Each non-null shape handle is added to the compound using BRep_Builder.
 * Null handles in the array are skipped with a warning.
 *
 *   ctx           – a valid context handle (used for logging; may be NULL)
 *   shapeHandles  – array of shape handles to combine into the compound
 *   numShapes     – number of shape handles (must be >= 0)
 *   outHandle     – receives the new compound shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_compound_make(
    XbimContextHandle         ctx,
    const XbimShapeHandle*    shapeHandles,
    int                       numShapes,
    XbimShapeHandle*          outHandle);

/*
 * Sew an array of shapes together using BRepBuilderAPI_Sewing.
 * Joins adjacent faces/shells that share edges within the given tolerance.
 * Useful for assembling a watertight shell from loose faces.
 *
 *   ctx           – a valid context handle (used for logging; may be NULL)
 *   shapeHandles  – array of shape handles to sew together
 *   numShapes     – number of shape handles (must be >= 1)
 *   tolerance     – sewing tolerance (must be > 0)
 *   outHandle     – receives the sewn shape handle
 *
 * Returns XBIM_OK on success; XBIM_NULL_SHAPE if the result is empty.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_compound_sew(
    XbimContextHandle         ctx,
    const XbimShapeHandle*    shapeHandles,
    int                       numShapes,
    double                    tolerance,
    XbimShapeHandle*          outHandle);

/*
 * Perform a boolean cut (difference) on a compound shape.
 * Subtracts the tool shape from the compound. If the compound is empty,
 * returns XBIM_NULL_SHAPE. If the tool is empty, the compound is returned.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   compoundHandle   – the compound shape to cut from (must not be NULL)
 *   toolHandle       – the shape to subtract (must not be NULL)
 *   fuzzyTolerance   – tolerance for the boolean operation (use model precision)
 *   outHasWarnings   – receives 1 if warnings were generated, 0 otherwise
 *   outHandle        – receives the resulting shape handle on success
 *
 * Returns XBIM_OK on success; XBIM_NULL_SHAPE if both inputs are empty
 * or the operation produces a null result.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_compound_cut(
    XbimContextHandle   ctx,
    XbimShapeHandle     compoundHandle,
    XbimShapeHandle     toolHandle,
    double              fuzzyTolerance,
    int*                outHasWarnings,
    XbimShapeHandle*    outHandle);

/*
 * Add a child shape to an existing compound.
 * Mutates the compound in-place using BRep_Builder::Add.
 *
 *   compoundHandle – a valid compound shape handle
 *   childHandle    – the shape to add (must not be NULL)
 *
 * Returns XBIM_OK on success; XBIM_INVALID_ARG if the handle is not a compound.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_compound_add(
    XbimShapeHandle compoundHandle,
    XbimShapeHandle childHandle);

/*
 * Count the direct children of a compound shape.
 * Uses TopoDS_Iterator (not TopExp_Explorer) so only immediate children
 * are counted, not shapes nested inside sub-compounds or sub-solids.
 *
 *   handle    – a valid shape handle (should be a compound)
 *   outCount  – receives the number of direct children
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_compound_child_count(
    XbimShapeHandle handle,
    int*            outCount);

/*
 * Extract the direct children of a compound shape.
 * The caller must first call xbim_compound_child_count to determine
 * the required array size.
 *
 * Each returned handle is a new heap-allocated XbimShape_ that the
 * caller owns and must eventually destroy with xbim_shape_destroy.
 *
 *   handle      – a valid shape handle (should be a compound)
 *   outHandles  – caller-allocated array of at least *count entries
 *   count       – on input: capacity of outHandles array
 *                 on output: actual number of children written
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_compound_get_children(
    XbimShapeHandle     handle,
    XbimShapeHandle*    outHandles,
    int*                count);

#pragma endregion

#pragma region Vertex Operations

/*
 * Build a vertex at the given 3D point with the specified tolerance.
 * Uses BRep_Builder::MakeVertex to construct a TopoDS_Vertex.
 *
 *   ctx         – a valid context handle (used for logging; may be NULL)
 *   x, y, z     – 3D coordinates of the vertex point
 *   tolerance   – geometric tolerance for the vertex (must be positive)
 *   outHandle   – receives the new vertex shape handle
 *
 * Returns XBIM_OK on success; XBIM_INVALID_ARG if tolerance <= 0 or
 * outHandle is NULL.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_vertex_build(
    XbimContextHandle ctx,
    double x, double y, double z,
    double tolerance,
    XbimShapeHandle* outHandle);

/*
 * Retrieve the 3D coordinates of a vertex.
 * Uses BRep_Tool::Pnt to extract the point from a TopoDS_Vertex.
 *
 *   vertexHandle – a shape handle containing a TopoDS_Vertex
 *   outX, outY, outZ – receive the 3D coordinates
 *
 * Returns XBIM_OK on success; XBIM_INVALID_ARG if the handle is not a
 * vertex or output pointers are NULL.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_vertex_point(
    XbimShapeHandle vertexHandle,
    double* outX, double* outY, double* outZ);

/*
 * Get the tolerance of a vertex. Uses BRep_Tool::Tolerance.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_vertex_tolerance(
    XbimShapeHandle vertexHandle,
    double*         outTolerance);

#pragma endregion

#pragma region Wire Queries

/*
 * Compute the total arc length of a wire.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_length(
    XbimShapeHandle wireHandle,
    double*         outLength);

/*
 * Compute the contour area enclosed by a wire.
 * Uses a shoelace-based projection. Not the surface area — the flat
 * polygon area of the wire outline.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_contour_area(
    XbimShapeHandle wireHandle,
    double*         outArea);

#pragma endregion

#pragma region Face Operations

/*
 * Build a face from an elementary surface with no boundary wires.
 * The surface type is specified as an XbimSurfaceType enum value.
 * Produces an unbounded (infinite-extent) face trimmed by OCCT defaults.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   surfaceType      – one of XBIM_SURFACE_PLANE/CYLINDRICAL/SPHERICAL
 *   originX/Y/Z      – surface placement origin
 *   zDirX/Y/Z        – surface placement Z direction
 *   xDirX/Y/Z        – surface placement X direction
 *   radius           – radius for cylindrical/spherical surfaces (ignored for plane)
 *   tolerance        – geometric tolerance for face construction
 *   outHandle        – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_build_from_surface(
    XbimContextHandle ctx,
    int               surfaceType,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    double tolerance,
    XbimShapeHandle*  outHandle);

/*
 * Build an unbounded face from any pre-built surface handle.
 * Produces an infinite face (no boundary wires) covering the full parametric
 * domain of the surface. Use tolerance to constrain the internal precision.
 *
 *   ctx           – a valid context handle (used for logging; may be NULL)
 *   surfaceHandle – handle to a Geom_Surface (from xbim_surface_build_*)
 *   tolerance     – modelling precision (e.g. model MinimumGap)
 *   outHandle     – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_build_unbounded_from_surface(
    XbimContextHandle  ctx,
    XbimSurfaceHandle  surfaceHandle,
    double             tolerance,
    XbimShapeHandle*   outHandle);

/*
 * Build a bounded face from a surface using its natural U parameter range
 * and an explicit extrusion depth for the V range [0, depth].
 * Used for Geom_SurfaceOfLinearExtrusion whose V direction is infinite.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_build_surface_with_depth(
    XbimContextHandle  ctx,
    XbimSurfaceHandle  surfaceHandle,
    double             depth,
    double             tolerance,
    XbimShapeHandle*   outHandle);

/*
 * Build a bounded face using the surface's natural parameter bounds.
 * Used for surfaces like Geom_SurfaceOfRevolution where an unbounded face
 * cannot be constructed but the natural bounds (U=[0,2π], V from basis curve)
 * are finite and well-defined.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_build_surface_natural_bounds(
    XbimContextHandle  ctx,
    XbimSurfaceHandle  surfaceHandle,
    double             tolerance,
    XbimShapeHandle*   outHandle);

/*
 * Build a planar face from a closed wire.
 * The wire must define a planar polygon; OCCT infers the plane automatically.
 *
 *   ctx          – a valid context handle (used for logging; may be NULL)
 *   wireHandle   – a shape handle containing a TopoDS_Wire (must be closed and planar)
 *   outHandle    – receives the new face shape handle
 *
 * Returns XBIM_OK on success; XBIM_INVALID_ARG if the handle is not a wire.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_build_from_wire(
    XbimContextHandle ctx,
    XbimShapeHandle   wireHandle,
    XbimShapeHandle*  outHandle);

/*
 * Build an advanced face with a surface, outer wire, optional inner wires,
 * and orientation control.
 *
 * The outer wire is oriented counter-clockwise (CCW) automatically.
 * Inner wires are oriented clockwise (CW) to define holes.
 * For non-planar surfaces, parametric curves (pcurves) are added to the wire
 * edges via ShapeFix_Wire.
 *
 *   ctx                – a valid context handle (used for logging; may be NULL)
 *   surfaceType        – one of XBIM_SURFACE_PLANE/CYLINDRICAL/SPHERICAL
 *   originX/Y/Z        – surface placement origin
 *   zDirX/Y/Z          – surface placement Z direction
 *   xDirX/Y/Z          – surface placement X direction
 *   radius             – radius for cylindrical/spherical (ignored for plane)
 *   outerWireHandle    – shape handle for the outer boundary wire (or face to extract wire from)
 *   innerWireHandles   – array of shape handles for inner boundary wires/faces (may be NULL)
 *   numInnerWires      – number of inner wire handles (0 if no holes)
 *   tolerance          – geometric tolerance for face and pcurve construction
 *   sameSense          – if non-zero, face normal agrees with surface normal; if zero, reversed
 *   outHandle          – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_build_advanced(
    XbimContextHandle        ctx,
    int                      surfaceType,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    XbimShapeHandle          outerWireHandle,
    const XbimShapeHandle*   innerWireHandles,
    int                      numInnerWires,
    double                   tolerance,
    int                      sameSense,
    XbimShapeHandle*         outHandle);

/*
 * Build an advanced face from a pre-built surface handle.
 * Unlike xbim_face_build_advanced which constructs the surface from type codes,
 * this variant accepts any XbimSurfaceHandle (including B-spline surfaces).
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   surfaceHandle    – pre-built surface handle (plane, cylinder, BSpline, etc.)
 *   outerWireHandle  – the outer boundary wire
 *   innerWireHandles – optional array of inner boundary wires (holes)
 *   numInnerWires    – number of inner wires (0 if none)
 *   tolerance        – precision tolerance for pcurve fitting
 *   sameSense        – 1 if face normal matches surface normal, 0 to reverse
 *   outHandle        – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_build_advanced_with_surface(
    XbimContextHandle        ctx,
    XbimSurfaceHandle        surfaceHandle,
    XbimShapeHandle          outerWireHandle,
    const XbimShapeHandle*   innerWireHandles,
    int                      numInnerWires,
    double                   tolerance,
    int                      sameSense,
    XbimShapeHandle*         outHandle);

/*
 * Compute the surface area of a face shape.
 * Uses BRepGProp::SurfaceProperties to calculate the area.
 *
 *   faceHandle – a valid shape handle (typically a face, but works for any shape)
 *   outArea    – receives the surface area on success
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_area(
    XbimShapeHandle faceHandle,
    double*         outArea);

/*
 * Compute the total perimeter (sum of edge lengths) of a face via linear
 * properties.
 *
 *   faceHandle   – a valid face shape handle
 *   outPerimeter – receives the perimeter length on success
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_perimeter(
    XbimShapeHandle faceHandle,
    double*         outPerimeter);

/*
 * Compute the outward-pointing normal of a face at a given parametric point.
 * If u and v are NaN, the normal is evaluated at the parametric centre.
 *
 *   faceHandle     – a valid face shape handle
 *   u              – parametric U coordinate (NaN for centre)
 *   v              – parametric V coordinate (NaN for centre)
 *   outNormalX/Y/Z – receives the normal vector components
 *
 * Returns XBIM_OK on success; XBIM_INVALID_ARG if the handle is not a face.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_normal(
    XbimShapeHandle faceHandle,
    double          u,
    double          v,
    double*         outNormalX,
    double*         outNormalY,
    double*         outNormalZ);

/*
 * Compute the surface normal at a 3D point projected onto the face.
 * Projects the point onto the face surface using ShapeAnalysis_Surface::ValueOfUV,
 * then evaluates the normal at that UV via GeomLProp_SLProps.
 *
 *   faceHandle         – a valid shape handle containing a TopoDS_Face
 *   pointX/Y/Z         – the 3D point to project
 *   precision           – tolerance for the UV projection
 *   tolerance           – tolerance for the surface property evaluation
 *   outNormalX/Y/Z     – receives the normal vector components
 *
 * Returns XBIM_OK on success; XBIM_INVALID_ARG if not a face.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_normal_at_point(
    XbimShapeHandle faceHandle,
    double          pointX,
    double          pointY,
    double          pointZ,
    double          precision,
    double          tolerance,
    double*         outNormalX,
    double*         outNormalY,
    double*         outNormalZ);

/*
 * Check whether a face is facing away from a given direction.
 * Computes the face normal at the parametric centre and compares the angle
 * with the given direction vector. Returns XBIM_TRUE if facing away.
 */
XBIM_EXPORT int XBIM_CALL xbim_face_is_facing_away(
    XbimShapeHandle faceHandle,
    double          dirX,
    double          dirY,
    double          dirZ);

/*
 * Get the geometric tolerance of a face.
 * Uses BRep_Tool::Tolerance.
 *
 *   faceHandle    – a valid shape handle containing a TopoDS_Face
 *   outTolerance  – receives the tolerance value
 *
 * Returns XBIM_OK on success; XBIM_INVALID_ARG if not a face.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_tolerance(
    XbimShapeHandle faceHandle,
    double*         outTolerance);

/*
 * Extract the underlying Geom_Surface from a face.
 * Returns both a surface handle and an integer surface type code
 * matching the Xbim.Geometry.Abstractions.XSurfaceType enum.
 *
 *   faceHandle     – a valid shape handle containing a TopoDS_Face
 *   outSurface     – receives the new surface handle (caller owns)
 *   outSurfaceType – receives the surface type code
 *
 * Returns XBIM_OK on success; XBIM_NULL_SHAPE if the face has no surface.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_get_surface(
    XbimShapeHandle    faceHandle,
    XbimSurfaceHandle* outSurface,
    int*               outSurfaceType);

/*
 * Add inner wires (holes) to a face. Returns a new face with the wires added.
 *
 *   faceHandle   – a valid face shape handle
 *   wireHandles  – array of wire shape handles to add
 *   wireCount    – number of wires
 *   outHandle    – receives the new face with inner wires
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_add_wires(
    XbimShapeHandle     faceHandle,
    XbimShapeHandle*    wireHandles,
    int                 wireCount,
    XbimShapeHandle*    outHandle);

/*
 * Repair a face using ShapeFix_Shape.
 * Returns the fixed shape — a single face, or a shell/compound containing
 * multiple faces when the fixer splits the input (e.g. multiple outer bounds).
 * Use xbim_shape_sub_shapes with TopAbs_FACE to extract individual faces.
 *
 *   faceHandle – a valid face shape handle
 *   tolerance  – geometric tolerance for the fix
 *   outHandle  – receives the fixed shape handle
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_fix(
    XbimShapeHandle     faceHandle,
    double              tolerance,
    XbimShapeHandle*    outHandle);

#pragma endregion

#pragma region Wire Construction

/*
 * Build a wire from a sequence of edge shape handles.
 * The edges are added to the wire in the order provided using BRep_Builder.
 * NULL or non-edge handles are skipped with a warning.
 *
 *   ctx           – a valid context handle (used for logging; may be NULL)
 *   edgeHandles   – array of shape handles containing TopoDS_Edge shapes
 *   numEdges      – number of elements in edgeHandles (must be > 0)
 *   outHandle     – receives the new wire shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_from_edges(
    XbimContextHandle        ctx,
    const XbimShapeHandle*   edgeHandles,
    int                      numEdges,
    XbimShapeHandle*         outHandle);

/*
 * Build a wire from a sequence of 3D curve handles with proper vertex connectivity.
 * Creates edges with shared vertices, tolerance adjustment, and
 * periodic/non-periodic curve transition handling (line geometry is rebuilt to
 * match arc endpoints).
 *
 * Curves should already have SameSense applied (reversed via xbim_curve_reverse
 * where SameSense=false) before calling this function.
 *
 *   ctx           – a valid context handle (used for logging; may be NULL)
 *   curveHandles  – array of XbimCurveHandle (Geom_Curve) in wire order
 *   numCurves     – number of elements in curveHandles (must be > 0)
 *   tolerance     – minimum vertex tolerance (typically model precision)
 *   gapSize       – maximum allowed gap between adjacent segment endpoints
 *   outHandle     – receives the new wire shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_from_curves(
    XbimContextHandle         ctx,
    const XbimCurveHandle*    curveHandles,
    int                       numCurves,
    double                    tolerance,
    double                    gapSize,
    XbimShapeHandle*          outHandle);

/*
 * Build a 3D polyline wire from an array of point coordinates.
 * Points that are within tolerance of the previous vertex are merged.
 * If the first and last points are within tolerance, the wire is marked closed.
 *
 *   ctx         – a valid context handle (used for logging; may be NULL)
 *   pointsXYZ   – flat array of [x0,y0,z0, x1,y1,z1, ...] coordinates
 *   numPoints   – number of 3D points (array length / 3; must be >= 2)
 *   tolerance   – minimum segment length; shorter segments are merged
 *   outHandle   – receives the new wire shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_polyline(
    XbimContextHandle ctx,
    const double*     pointsXYZ,
    int               numPoints,
    double            tolerance,
    XbimShapeHandle*  outHandle);

/*
 * Build a polygon wire from an array of point coordinates.
 * Each consecutive pair of points defines an edge. Degenerate edges
 * (length < Precision::Confusion) are skipped.
 * If closed is non-zero, an additional edge is added from the last point
 * back to the first point to close the polygon.
 *
 *   ctx         – a valid context handle (used for logging; may be NULL)
 *   pointsXYZ   – flat array of [x0,y0,z0, x1,y1,z1, ...] coordinates
 *   numPoints   – number of 3D points (array length / 3; must be >= 2)
 *   closed      – if non-zero, close the polygon (last→first edge added)
 *   outHandle   – receives the new wire shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_polygon(
    XbimContextHandle ctx,
    const double*     pointsXYZ,
    int               numPoints,
    int               closed,
    XbimShapeHandle*  outHandle);

/*
 * Check whether a wire is closed (first and last points coincide).
 * Uses BRepAdaptor_CompCurve to evaluate the wire endpoints and compare
 * them within the given tolerance.
 *
 *   wireHandle – a valid shape handle containing a TopoDS_Wire
 *   tolerance  – distance tolerance for comparing endpoints
 *   outClosed  – receives 1 if closed, 0 if open
 *
 * Returns XBIM_OK on success; XBIM_INVALID_ARG if the handle is not a wire.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_is_closed(
    XbimShapeHandle wireHandle,
    double          tolerance,
    int*            outClosed);

/*
 * Project a 3D point onto a wire and return the parametric position.
 * The parameter is accumulated arc-length across the wire's edges,
 * matching the legacy NWireFactory::GetParameter behavior.
 *
 *   wireHandle – a valid shape handle containing a TopoDS_Wire
 *   pointX/Y/Z – the 3D point to project
 *   tolerance  – projection tolerance
 *   outParam   – receives the parametric position on the wire
 *
 * Returns XBIM_OK on success; XBIM_ERROR if the point is not on the wire.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_get_parameter(
    XbimShapeHandle wireHandle,
    double          pointX,
    double          pointY,
    double          pointZ,
    double          tolerance,
    double*         outParam);

/*
 * Trim a wire by parametric range.
 *
 * For single-edge wires with angular conics (circle/ellipse), applies
 * radianFactor conversion and normalizes to 0..2pi.  For multi-edge
 * wires, walks intervals and trims first/last edges at boundaries.
 *
 *   ctx          – a valid context handle (used for logging; may be NULL)
 *   wireHandle   – the basis wire to trim
 *   u1           – start parameter
 *   u2           – end parameter
 *   sameSense    – 1 for same sense, 0 for opposite
 *   tolerance    – geometric tolerance
 *   radianFactor – angle-to-radians conversion factor (from context)
 *   outHandle    – receives the trimmed wire
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_trimmed(
    XbimContextHandle ctx,
    XbimShapeHandle   wireHandle,
    double            u1,
    double            u2,
    int               sameSense,
    double            tolerance,
    double            radianFactor,
    XbimShapeHandle*  outHandle);

/*
 * Trim a wire by arc-length positions.
 *
 * Walks edges sequentially, accumulating geometric lengths.  Trims the
 * first edge at arcStart and the last edge at arcEnd; edges fully inside
 * the range are taken whole; edges outside are skipped.
 *
 *   ctx       – a valid context handle (used for logging; may be NULL)
 *   wireHandle – the basis wire to trim
 *   arcStart  – start position in arc-length units from wire start
 *   arcEnd    – end position in arc-length units from wire start
 *   tolerance – geometric tolerance for edge construction
 *   outHandle – receives the trimmed wire
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_trimmed_by_length(
    XbimContextHandle ctx,
    XbimShapeHandle   wireHandle,
    double            arcStart,
    double            arcEnd,
    double            tolerance,
    XbimShapeHandle*  outHandle);

/*
 * Trim a wire with optional Cartesian point projection.
 *
 * If preferCartesian is true, projects p1 and p2 onto the wire to obtain
 * parametric positions; otherwise uses u1/u2 directly.  Delegates to
 * xbim_wire_build_trimmed for the actual trimming.
 *
 *   ctx             – a valid context handle (used for logging; may be NULL)
 *   wireHandle      – the basis wire to trim
 *   p1X/Y/Z         – first trim point (used if preferCartesian)
 *   p2X/Y/Z         – second trim point (used if preferCartesian)
 *   u1              – first parametric value (fallback)
 *   u2              – second parametric value (fallback)
 *   preferCartesian – 1 to project points, 0 to use parametric values
 *   sameSense       – 1 for same sense, 0 for opposite
 *   tolerance       – geometric tolerance
 *   radianFactor    – angle-to-radians conversion factor
 *   outHandle       – receives the trimmed wire
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_trimmed_by_points(
    XbimContextHandle ctx,
    XbimShapeHandle   wireHandle,
    double            p1X,
    double            p1Y,
    double            p1Z,
    double            p2X,
    double            p2Y,
    double            p2Z,
    double            u1,
    double            u2,
    int               preferCartesian,
    int               sameSense,
    double            tolerance,
    double            radianFactor,
    XbimShapeHandle*  outHandle);

/*
 * Fillet a wire by inserting circular arcs at each interior vertex.
 *
 * Processes consecutive edge pairs, applying BRepFilletAPI_MakeFillet2d
 * at each shared vertex. If a vertex cannot be filleted (e.g. edges are
 * collinear or too short), that vertex is skipped and the original edges
 * are preserved. If the wire is closed, the first/last vertex is also
 * filleted.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   wireHandle       – a shape handle containing a TopoDS_Wire to fillet
 *   filletRadius     – radius of the fillet arcs (must be > 0)
 *   tolerance        – model precision tolerance for closed-wire detection
 *   outHandle        – receives the new filleted wire shape handle
 *
 * Returns XBIM_OK on success, XBIM_ERROR on failure.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_fillet(
    XbimContextHandle ctx,
    XbimShapeHandle   wireHandle,
    double            filletRadius,
    double            tolerance,
    XbimShapeHandle*  outHandle);

#pragma endregion

#pragma region Edge Operations

/*
 * Build a straight edge (line segment) between two 3D points.
 * The points must not be coincident (distance > Precision::Confusion).
 *
 *   ctx                – a valid context handle (used for logging; may be NULL)
 *   startX/Y/Z         – coordinates of the start point
 *   endX/Y/Z           – coordinates of the end point
 *   outHandle          – receives the new edge shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_build_line(
    XbimContextHandle ctx,
    double startX, double startY, double startZ,
    double endX,   double endY,   double endZ,
    XbimShapeHandle* outHandle);

/*
 * Build an edge as a sub-range of the 3D curve from an existing edge.
 * Extracts the Geom_Curve from curveEdgeHandle and creates a new edge
 * bounded by the given parametric range [param1, param2].
 * Parameters are clamped to the source curve's valid range.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   curveEdgeHandle  – a valid shape handle containing a TopoDS_Edge with a 3D curve
 *   param1           – start parameter on the curve
 *   param2           – end parameter on the curve
 *   outHandle        – receives the new edge shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_build_from_curve(
    XbimContextHandle ctx,
    XbimShapeHandle   curveEdgeHandle,
    double            param1,
    double            param2,
    XbimShapeHandle*  outHandle);

/*
 * Build a circular arc edge from center, axis normal, radius, and angle range.
 * Angles are in radians. The arc spans from startAngle to endAngle around
 * the circle defined by the given center and normal direction.
 *
 *   ctx                – a valid context handle (used for logging; may be NULL)
 *   centerX/Y/Z        – center point of the circle
 *   normalX/Y/Z        – axis normal direction (defines the plane of the circle)
 *   radius             – radius of the circle (must be positive)
 *   startAngle         – start angle in radians
 *   endAngle           – end angle in radians
 *   outHandle          – receives the new edge shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_build_circle_arc(
    XbimContextHandle ctx,
    double centerX, double centerY, double centerZ,
    double normalX, double normalY, double normalZ,
    double radius,
    double startAngle,
    double endAngle,
    XbimShapeHandle* outHandle);

/*
 * Build a circular arc edge passing through three points.
 * Uses OCCT GC_MakeArcOfCircle to find the unique circle through the three
 * points and construct a trimmed arc from p1 through p2 to p3.
 * If the points are collinear, falls back to a straight line from p1 to p3.
 *
 *   ctx        – a valid context handle (used for logging; may be NULL)
 *   p1X/Y/Z    – first point (arc start)
 *   p2X/Y/Z    – second point (arc passes through)
 *   p3X/Y/Z    – third point (arc end)
 *   outHandle  – receives the new edge shape handle (caller owns)
 *
 * Returns XBIM_OK on success; XBIM_INVALID_ARG if points are coincident.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_build_circle_arc_3pt(
    XbimContextHandle ctx,
    double p1X, double p1Y, double p1Z,
    double p2X, double p2Y, double p2Z,
    double p3X, double p3Y, double p3Z,
    XbimShapeHandle* outHandle);

/*
 * Build an edge from a pre-built curve handle with start/end vertex positions.
 * Used for IFC edge curves in advanced BRep faces. If start and end positions
 * are coincident (within tolerance), builds a closed edge (seam).
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   curveHandle      – a pre-built curve handle (line, circle, BSpline, etc.)
 *   startX/Y/Z       – coordinates of the start vertex
 *   endX/Y/Z         – coordinates of the end vertex
 *   sameSense        – 1 if edge direction matches curve parametric direction
 *   tolerance        – vertex proximity tolerance
 *   outHandle        – receives the new edge shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_build_from_curve_handle(
    XbimContextHandle ctx,
    XbimCurveHandle   curveHandle,
    double startX, double startY, double startZ,
    double endX,   double endY,   double endZ,
    int              sameSense,
    double           tolerance,
    XbimShapeHandle* outHandle);

/*
 * Build an edge from a pre-built 2D curve handle with start/end positions.
 * Uses BRepLib_MakeEdge2d followed by BRepLib::BuildCurve3d to produce
 * a valid BRep edge with both 2D and 3D curve representations.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   curve2dHandle    – a pre-built 2D curve handle
 *   startX/Y         – coordinates of the start point (2D)
 *   endX/Y           – coordinates of the end point (2D)
 *   sameSense        – 1 if edge direction matches curve parametric direction
 *   tolerance        – point proximity tolerance
 *   outHandle        – receives the new edge shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_build_from_curve2d_handle(
    XbimContextHandle  ctx,
    XbimCurve2dHandle  curve2dHandle,
    double startX, double startY,
    double endX,   double endY,
    int                sameSense,
    double             tolerance,
    XbimShapeHandle*   outHandle);

/*
 * Build an edge from a bounded 3D curve handle (circle, ellipse, BSpline, trimmed curve, etc.).
 * The full parametric range of the curve is used.
 * Fails with XBIM_INVALID_ARG for unbounded Geom_Line — callers must trim it first.
 *
 *   ctx        – a valid context handle (used for logging; may be NULL)
 *   curveHandle – a pre-built 3D curve handle
 *   outHandle  – receives the new edge shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_from_curve_handle(
    XbimContextHandle ctx,
    XbimCurveHandle   curveHandle,
    XbimShapeHandle*  outHandle);

/*
 * Build an edge from a bounded 2D curve handle.
 * The full parametric range of the curve is used.
 * A 3D curve representation is generated via BRepLib::BuildCurve3d.
 * Fails with XBIM_INVALID_ARG for unbounded Geom2d_Line.
 *
 *   ctx           – a valid context handle (used for logging; may be NULL)
 *   curve2dHandle – a pre-built 2D curve handle
 *   outHandle     – receives the new edge shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_from_curve2d_handle(
    XbimContextHandle  ctx,
    XbimCurve2dHandle  curve2dHandle,
    XbimShapeHandle*   outHandle);

/*
 * Compute the length of an edge.
 * Uses GCPnts_AbscissaPoint::Length on the edge's BRepAdaptor_Curve.
 *
 *   edgeHandle – a valid shape handle containing a TopoDS_Edge
 *   outLength  – receives the edge length on success
 *
 * Returns XBIM_OK on success; XBIM_INVALID_ARG if the handle is not an edge.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_length(
    XbimShapeHandle edgeHandle,
    double*         outLength);

/*
 * Get the geometric tolerance of an edge.
 * Uses BRep_Tool::Tolerance.
 *
 *   edgeHandle    – a valid shape handle containing a TopoDS_Edge
 *   outTolerance  – receives the tolerance value
 *
 * Returns XBIM_OK on success; XBIM_INVALID_ARG if not an edge.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_tolerance(
    XbimShapeHandle edgeHandle,
    double*         outTolerance);

/*
 * Extract the start and end vertices of an edge.
 * Uses TopExp::Vertices. Either output handle may be NULL if the
 * edge has no corresponding vertex (e.g. periodic/closed edges).
 *
 *   edgeHandle – a valid shape handle containing a TopoDS_Edge
 *   outStart   – receives the first vertex handle (caller owns)
 *   outEnd     – receives the last vertex handle (caller owns)
 *
 * Returns XBIM_OK on success; XBIM_INVALID_ARG if not an edge.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_vertices(
    XbimShapeHandle  edgeHandle,
    XbimShapeHandle* outStart,
    XbimShapeHandle* outEnd);

/*
 * Extract the 3D curve geometry from an edge.
 * Uses BRep_Tool::Curve to obtain the underlying Geom_Curve and its
 * parameter range [p1, p2].
 *
 *   edgeHandle – a valid shape handle containing a TopoDS_Edge
 *   outCurve   – receives the curve handle (caller owns, must destroy)
 *   outParam1  – receives the first parameter
 *   outParam2  – receives the last parameter
 *
 * Returns XBIM_OK on success; XBIM_NULL_SHAPE if the edge has no 3D curve.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_edge_get_curve(
    XbimShapeHandle  edgeHandle,
    XbimCurveHandle* outCurve,
    double*          outParam1,
    double*          outParam2);

#pragma endregion

#pragma region Shell Operations

/*
 * Build a shell from an array of face shape handles.
 * Each valid face handle is added to the shell using BRep_Builder.
 * Non-face shapes are explored for sub-faces. NULL or null shapes are
 * skipped with a warning.
 *
 *   ctx           – a valid context handle (used for logging; may be NULL)
 *   faceHandles   – array of shape handles containing face shapes
 *   numFaces      – number of elements in faceHandles (must be >= 1)
 *   tolerance     – geometric tolerance for shell construction
 *   outHandle     – receives the new shell shape handle
 *
 * Returns XBIM_OK on success; XBIM_NULL_SHAPE if no valid faces were added.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shell_build_from_faces(
    XbimContextHandle        ctx,
    const XbimShapeHandle*   faceHandles,
    int                      numFaces,
    double                   tolerance,
    XbimShapeHandle*         outHandle);

/*
 * Validate and repair a shell's face orientation.
 * First checks the shell with BRepCheck_Shell::Orientation. If the
 * orientation is correct, the shell is returned as-is. Otherwise,
 * ShapeFix_Shell is used to repair face orientations.
 *
 *   ctx           – a valid context handle (used for logging; may be NULL)
 *   shellHandle   – shape handle containing a TopoDS_Shell (or a shape with a sub-shell)
 *   tolerance     – precision for the shape fixer
 *   outIsFixed    – receives 1 if the shell is valid (original or repaired), 0 otherwise
 *   outHandle     – receives the resulting shell (or compound if split)
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shell_sew(
    XbimContextHandle   ctx,
    XbimShapeHandle     shellHandle,
    double              tolerance,
    int*                outIsFixed,
    XbimShapeHandle*    outHandle);

/*
 * Convert a closed shell into a solid using BRepBuilderAPI_MakeSolid.
 * The input must contain a TopoDS_Shell (directly or as a sub-shape).
 *
 *   ctx           – a valid context handle (used for logging; may be NULL)
 *   shellHandle   – shape handle containing a TopoDS_Shell
 *   outHandle     – receives the new solid shape handle
 *
 * Returns XBIM_OK on success; XBIM_NULL_SHAPE if the shell cannot form a solid.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shell_make_solid(
    XbimContextHandle   ctx,
    XbimShapeHandle     shellHandle,
    XbimShapeHandle*    outHandle);

/*
 * Build a closed shell from individual faces using BRepOffsetAPI_Sewing,
 * then convert to a solid via ShapeFix_Solid. This is the correct way to
 * build a solid from faceted BRep faces that don't share edges.
 *
 * The sewing step merges coincident edges between adjacent faces,
 * creating proper topological connectivity. The ShapeFix_Solid step
 * fixes orientation and converts the shell to a valid solid.
 *
 *   ctx           – a valid context handle (used for logging; may be NULL)
 *   faceHandles   – array of face shape handles
 *   numFaces      – number of faces in the array (must be >= 4)
 *   tolerance     – sewing tolerance for merging coincident edges
 *   outHandle     – receives the resulting solid shape handle
 *
 * Returns XBIM_OK on success; XBIM_NULL_SHAPE if sewing or solid creation fails.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shell_build_closed_shell(
    XbimContextHandle        ctx,
    const XbimShapeHandle*   faceHandles,
    int                      numFaces,
    double                   tolerance,
    XbimShapeHandle*         outHandle);

/*
 * Build a shell (or solid) from connected planar faces using shared topology.
 *
 * Unlike xbim_shell_build_closed_shell which sews independent faces,
 * this function builds proper topological connectivity from the start by
 * deduplicating vertices within tolerance and sharing edges between
 * adjacent faces that reference the same vertex pair.
 *
 * Face data encoding:
 *   The faceData array is a packed sequence of face descriptors:
 *     [numBounds, bound0..., bound1..., ...]
 *   Each bound is:
 *     [numPoints, isOuter, pt0_idx, pt1_idx, ..., ptN_idx, pt0_idx]
 *   Where:
 *     - numPoints is the number of point indices that follow (including the
 *       repeated closing index)
 *     - isOuter is 1 for the outer bound, 0 for inner (hole) bounds
 *     - pt_idx values are 0-based indices into allPointsXYZ
 *     - The last index repeats the first to close the loop
 *
 *   ctx            - context handle (for logging; may be NULL)
 *   allPointsXYZ   - flat array [x0,y0,z0, x1,y1,z1, ...] of all unique points
 *   numPoints      - number of points (array length = numPoints * 3)
 *   faceData       - packed face topology (see encoding above)
 *   faceDataLength - total number of ints in faceData
 *   numFaces       - number of faces encoded in faceData
 *   tolerance        - precision for vertex merging (typically MinimumGap)
 *   makeSolid        - if non-zero, convert the shell to a solid with orientation fix
 *   upgradeFaceSets  - if non-zero and makeSolid is set, detect multi-solid shells
 *                      and split them into individual solids (returns compound).
 *                      If zero, just does a simple shell-to-solid conversion.
 *   outHandle        - receives the resulting shape (solid, compound, or shell)
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shell_build_connected_face_set(
    XbimContextHandle  ctx,
    const double*      allPointsXYZ,
    int                numPoints,
    const int*         faceData,
    int                faceDataLength,
    int                numFaces,
    double             tolerance,
    int                makeSolid,
    int                upgradeFaceSets,
    XbimShapeHandle*   outHandle);

#pragma endregion

#pragma region Curve Lifecycle

/*
 * Build an infinite 3D line curve from an origin point and direction.
 *
 *   ctx            – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z    – a point on the line
 *   dirX/Y/Z       – direction of the line (must be non-zero)
 *   outHandle      – receives the new curve handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_line_3d(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double dirX,    double dirY,    double dirZ,
    XbimCurveHandle* outHandle);

/*
 * Build a 3D circle curve from center, axis placement, and radius.
 *
 *   ctx            – a valid context handle (used for logging; may be NULL)
 *   centerX/Y/Z    – center point of the circle
 *   normalX/Y/Z    – axis normal direction (defines the plane of the circle)
 *   xDirX/Y/Z      – reference direction (X axis of placement; defines parameter 0)
 *   radius          – circle radius (must be positive)
 *   outHandle      – receives the new curve handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_circle_3d(
    XbimContextHandle ctx,
    double centerX, double centerY, double centerZ,
    double normalX, double normalY, double normalZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    XbimCurveHandle* outHandle);

/*
 * Build a 3D ellipse curve from center, axis placement, and IFC semi-axes.
 * Uses Geom_EllipseWithSemiAxes to handle IFC semantics: when semiAxis1
 * (along X) is smaller than semiAxis2 (along Y), the placement is rotated
 * by -PI/2 so OCCT's major axis aligns with IFC's SemiAxis2 direction.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   centerX/Y/Z      – center point of the ellipse
 *   normalX/Y/Z      – axis normal direction (defines the plane)
 *   xDirX/Y/Z        – X reference direction (SemiAxis1 direction)
 *   semiAxis1         – IFC SemiAxis1 length (along X, must be positive)
 *   semiAxis2         – IFC SemiAxis2 length (along Y, must be positive)
 *   outRotated        – receives 1 if axes were swapped (may be NULL)
 *   outHandle         – receives the new curve handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_ellipse_3d(
    XbimContextHandle ctx,
    double centerX,  double centerY,  double centerZ,
    double normalX,  double normalY,  double normalZ,
    double xDirX,    double xDirY,    double xDirZ,
    double semiAxis1, double semiAxis2,
    int* outRotated,
    XbimCurveHandle* outHandle);

/*
 * Build a 3D B-spline curve from control points, knots, multiplicities, and degree.
 * Optionally weighted (rational). Ports NCurveFactory::BuildBSplineCurve3d and
 * NCurveFactory::BuildRationalBSplineCurve3d.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   polesXYZ         – flat array [x0,y0,z0, x1,y1,z1, ...] (numPoles * 3 elements)
 *   numPoles         – number of control points (must be >= 2)
 *   knots            – flat array of knot values (numKnots elements)
 *   numKnots         – number of distinct knots (must be >= 2)
 *   multiplicities   – multiplicity for each knot (numKnots elements)
 *   degree           – polynomial degree (must be >= 1)
 *   weights          – optional flat array of weights (numPoles elements; NULL for non-rational)
 *   outHandle        – receives the new curve handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_bspline(
    XbimContextHandle ctx,
    const double*     polesXYZ,
    int               numPoles,
    const double*     knots,
    int               numKnots,
    const int*        multiplicities,
    int               degree,
    const double*     weights,
    XbimCurveHandle*  outHandle);

/*
 * Build a trimmed 3D curve from a basis curve and parameter range.
 * Handles circle (GC_MakeArcOfCircle), ellipse with IFC semi-axis conversion
 * (GC_MakeArcOfEllipse + ConvertIfcTrimParameter), and generic curves
 * (Geom_TrimmedCurve).
 *
 *   ctx         – a valid context handle (used for logging; may be NULL)
 *   basisHandle – the unbounded or periodic basis curve to trim
 *   u1/u2       – trim parameter values
 *   sense       – nonzero for same-sense, zero for reversed
 *   outHandle   – receives the trimmed curve handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_trimmed_3d(
    XbimContextHandle   ctx,
    XbimCurveHandle     basisHandle,
    double u1, double u2,
    int sense,
    XbimCurveHandle*    outHandle);

/*
 * Build a bounded 3D line segment between two points.
 * Creates a Geom_Line from start to end, then trims it from 0 to the distance.
 *
 *   ctx          – a valid context handle (used for logging; may be NULL)
 *   x1/y1/z1     – start point
 *   x2/y2/z2     – end point
 *   outHandle    – receives the trimmed line handle
 *
 * Returns XBIM_OK on success; XBIM_INVALID_ARG if points are coincident.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_trimmed_line_3d(
    XbimContextHandle   ctx,
    double x1, double y1, double z1,
    double x2, double y2, double z2,
    XbimCurveHandle*    outHandle);

/*
 * Build a 3D circle from three non-collinear points.
 * Uses GC_MakeCircle. Returns XBIM_ERROR if points are collinear (caller
 * should handle the fallback to a line segment).
 *
 *   ctx          – a valid context handle (used for logging; may be NULL)
 *   x1/y1/z1     – first point
 *   x2/y2/z2     – second point (mid-arc)
 *   x3/y3/z3     – third point
 *   outHandle    – receives the circle handle (full circle, not trimmed)
 *
 * Returns XBIM_OK on success; XBIM_ERROR if collinear.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_circle_3pt_3d(
    XbimContextHandle   ctx,
    double x1, double y1, double z1,
    double x2, double y2, double z2,
    double x3, double y3, double z3,
    XbimCurveHandle*    outHandle);

/*
 * Build a circular arc (Geom_TrimmedCurve) from a 3D circle and parameter range.
 * Uses GC_MakeArcOfCircle. If !sense, parameters are swapped (legacy behavior).
 *
 *   ctx          – a valid context handle (used for logging; may be NULL)
 *   circleHandle – a handle wrapping a Geom_Circle
 *   u1/u2        – trim parameter values (radians)
 *   sense        – nonzero for same-sense, zero for reversed
 *   outHandle    – receives the arc handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_arc_of_circle_3d(
    XbimContextHandle   ctx,
    XbimCurveHandle     circleHandle,
    double u1, double u2,
    int sense,
    XbimCurveHandle*    outHandle);

/*
 * Destroy a curve handle and free its resources.
 * Passing NULL is a safe no-op.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_destroy(XbimCurveHandle handle);

#pragma endregion

#pragma region Curve Queries

/*
 * Get the parametric range of a curve.
 *   outFirst/outLast – receive the first and last parameter values
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_parameters(
    XbimCurveHandle handle,
    double*         outFirst,
    double*         outLast);

/*
 * Compute the arc length of a curve over its full parameter range.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_length(
    XbimCurveHandle handle,
    double*         outLength);

/*
 * Find the curve parameter corresponding to a given arc length measured from
 * the curve's first parameter.  Uses GCPnts_AbscissaPoint internally.
 *
 *   handle       – a valid curve handle
 *   arcLength    – target arc length from curve start
 *   tolerance    – computation tolerance (e.g. model precision)
 *   outParameter – receives the curve parameter at the requested arc length
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_parameter_at_length(
    XbimCurveHandle handle,
    double          arcLength,
    double          tolerance,
    double*         outParameter);

/*
 * Evaluate a point on the curve at parameter u.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_value(
    XbimCurveHandle handle,
    double          u,
    double*         outX, double* outY, double* outZ);

/*
 * Evaluate point and first derivative at parameter u.
 *   outPx..outPz – point coordinates
 *   outDx..outDz – first derivative vector
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_d1(
    XbimCurveHandle handle,
    double          u,
    double*         outPx, double* outPy, double* outPz,
    double*         outDx, double* outDy, double* outDz);

/*
 * Evaluate point, first derivative, and second derivative at parameter u.
 *   outPx..outPz   – point coordinates
 *   outD1x..outD1z – first derivative vector
 *   outD2x..outD2z – second derivative vector
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_d2(
    XbimCurveHandle handle,
    double          u,
    double*         outPx,  double* outPy,  double* outPz,
    double*         outD1x, double* outD1y, double* outD1z,
    double*         outD2x, double* outD2y, double* outD2z);

/*
 * Check whether a curve is closed (start point coincides with end point
 * within the given tolerance).
 *
 * Returns XBIM_TRUE if closed, XBIM_FALSE otherwise.
 */
XBIM_EXPORT int XBIM_CALL xbim_curve_is_closed(
    XbimCurveHandle handle,
    double          tolerance);

/*
 * Project a 3D point onto a curve and return the curve parameter.
 * Uses GeomLib_Tool::Parameter.
 *
 *   ctx         – context handle (used for logging; may be NULL)
 *   curveHandle – a valid 3D curve handle
 *   px, py, pz  – the point to project
 *   tolerance   – projection tolerance
 *   outParam    – receives the parameter value on the curve
 *
 * Returns XBIM_OK on success, XBIM_ERROR if the point cannot be projected.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_project_point_3d(
    XbimContextHandle   ctx,
    XbimCurveHandle     curveHandle,
    double px, double py, double pz,
    double tolerance,
    double* outParam);

/*
 * Reverse the direction of a curve in-place.
 *
 *   handle – a valid curve handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_reverse(XbimCurveHandle handle);

/*
 * Join multiple bounded 3D curves into a single B-spline curve.
 * Curves that cannot be added directly are approximated via point sampling.
 * Gaps between non-contiguous segments are filled with linear segments.
 *
 *   ctx       – a valid context handle (used for logging; may be NULL)
 *   curves    – array of curve handles (must wrap bounded curves)
 *   numCurves – number of curves in the array (>= 1)
 *   tolerance – gap tolerance for joining segments
 *   outHandle – receives the composite B-spline curve handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_composite_bspline(
    XbimContextHandle   ctx,
    XbimCurveHandle*    curves,
    int                 numCurves,
    double              tolerance,
    XbimCurveHandle*    outHandle);

/*
 * Build a 3D offset curve from a basis curve, an offset distance, and a
 * reference direction vector. Uses Geom_OffsetCurve(basis, offset, refDir).
 *
 *   ctx         – a valid context handle (used for logging; may be NULL)
 *   basisHandle – the basis curve to offset
 *   offset      – offset distance (positive = towards refDir cross tangent)
 *   refDirX/Y/Z – reference direction defining the offset plane normal
 *   outHandle   – receives the new offset curve handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_offset_3d(
    XbimContextHandle ctx,
    XbimCurveHandle   basisHandle,
    double            offset,
    double            refDirX, double refDirY, double refDirZ,
    XbimCurveHandle*  outHandle);

#pragma endregion

#pragma region Surface Operations

/*
 * Build an infinite plane surface from an origin point, normal direction,
 * and optional reference direction (X-axis).
 *
 *   ctx            – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z    – a point on the plane
 *   normalX/Y/Z    – plane normal direction (must be non-zero)
 *   refDirX/Y/Z    – reference direction (X-axis); if zero-length, OCCT auto-computes
 *   outHandle      – receives the new surface handle
 *   outRefDirX/Y/Z – receives the actual reference direction (X-axis) used
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_plane(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double normalX, double normalY, double normalZ,
    double refDirX,  double refDirY,  double refDirZ,
    XbimSurfaceHandle* outHandle,
    double* outRefDirX, double* outRefDirY, double* outRefDirZ);

/*
 * Build a cylindrical surface from an axis-2 placement and radius.
 * The cylinder axis is the Z direction of the placement.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin
 *   zDirX/Y/Z        – placement Z direction (cylinder axis)
 *   xDirX/Y/Z        – placement X direction (reference)
 *   radius           – cylinder radius (must be positive)
 *   outHandle        – receives the new surface handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_cylindrical(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    XbimSurfaceHandle* outHandle);

/*
 * Build a spherical surface from an axis-2 placement and radius.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin (center of sphere)
 *   zDirX/Y/Z        – placement Z direction
 *   xDirX/Y/Z        – placement X direction (reference)
 *   radius           – sphere radius (must be positive)
 *   outHandle        – receives the new surface handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_spherical(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double radius,
    XbimSurfaceHandle* outHandle);

/*
 * Build a B-spline surface from a grid of control points, knots, multiplicities,
 * and degrees. Optionally weighted (rational).
 *
 *   ctx                – a valid context handle (used for logging; may be NULL)
 *   polesXYZ           – flat row-major array of control points
 *                        [u0v0.x, u0v0.y, u0v0.z, u0v1.x, ..., uN-1vM-1.z]
 *                        (numPolesU * numPolesV * 3 elements)
 *   numPolesU          – number of control points in U direction (must be >= 2)
 *   numPolesV          – number of control points in V direction (must be >= 2)
 *   uKnots             – U knot values (numUKnots elements)
 *   numUKnots          – number of distinct U knots (must be >= 2)
 *   vKnots             – V knot values (numVKnots elements)
 *   numVKnots          – number of distinct V knots (must be >= 2)
 *   uMultiplicities    – U knot multiplicities (numUKnots elements)
 *   vMultiplicities    – V knot multiplicities (numVKnots elements)
 *   uDegree            – polynomial degree in U (must be >= 1)
 *   vDegree            – polynomial degree in V (must be >= 1)
 *   weights            – optional flat row-major weight array
 *                        (numPolesU * numPolesV elements; NULL for non-rational)
 *   outHandle          – receives the new surface handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_bspline(
    XbimContextHandle ctx,
    const double*     polesXYZ,
    int               numPolesU,
    int               numPolesV,
    const double*     uKnots,
    int               numUKnots,
    const double*     vKnots,
    int               numVKnots,
    const int*        uMultiplicities,
    const int*        vMultiplicities,
    int               uDegree,
    int               vDegree,
    const double*     weights,
    XbimSurfaceHandle* outHandle);

/*
 * Build a sectioned surface from cross-section polyline points and locations.
 *
 * Each cross-section is a polyline in local 2D space (z=0). The points are
 * transformed by the corresponding location to 3D world space. Ruled surface
 * strips are created between longitudinal wires connecting same-indexed points
 * across all sections, then sewn into a single compound shape.
 *
 *   ctx                  – a valid context handle
 *   pointsXYZ            – flat array of 3D points [numSections * numPointsPerSection * 3]
 *                          row-major: section 0 points, section 1 points, ...
 *   numSections          – number of cross-sections (must be >= 2)
 *   numPointsPerSection  – number of points per cross-section (must be >= 2)
 *   locations            – array of numSections location handles for positioning
 *   outHandle            – receives the new shape handle (sewn surface compound)
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_sectioned(
    XbimContextHandle          ctx,
    const double*              pointsXYZ,
    int                        numSections,
    int                        numPointsPerSection,
    const XbimLocationHandle*  locations,
    XbimShapeHandle*           outHandle);

/*
 * Build a surface of revolution by revolving a generatrix curve around an axis.
 *
 *   ctx          – a valid context handle (used for logging; may be NULL)
 *   curveHandle  – the generatrix curve to revolve
 *   axisOrigin   – point on the revolution axis (X, Y, Z)
 *   axisDir      – direction of the revolution axis (X, Y, Z)
 *   outHandle    – receives the new surface handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_revolution(
    XbimContextHandle  ctx,
    XbimCurveHandle    curveHandle,
    double axisOriginX, double axisOriginY, double axisOriginZ,
    double axisDirX,    double axisDirY,    double axisDirZ,
    XbimSurfaceHandle* outHandle);

/*
 * Build a surface of linear extrusion by sweeping a curve along a direction.
 *
 *   ctx          – a valid context handle (used for logging; may be NULL)
 *   curveHandle  – the generatrix curve to sweep
 *   dir          – extrusion direction (X, Y, Z); will be normalised
 *   posO/posZ/posX – IIfcSweptSurface.Position (origin, Z-axis, X-axis)
 *   hasPosition  – nonzero if the position parameters are valid and should
 *                  be applied as a transform on the resulting surface
 *   outHandle    – receives the new surface handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_linear_extrusion(
    XbimContextHandle  ctx,
    XbimCurveHandle    curveHandle,
    double dirX,    double dirY,    double dirZ,
    double posOX,   double posOY,   double posOZ,
    double posZX,   double posZY,   double posZZ,
    double posXX,   double posXY,   double posXZ,
    int    hasPosition,
    XbimSurfaceHandle* outHandle);

/*
 * Build a toroidal surface from an axis-2 placement, major radius, and minor radius.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – placement origin (centre of the torus)
 *   zDirX/Y/Z        – placement Z direction (torus axis)
 *   xDirX/Y/Z        – placement X direction (reference)
 *   majorRadius      – distance from the torus centre to the tube centre (must be >= 0)
 *   minorRadius      – radius of the tube cross-section (must be > 0)
 *   outHandle        – receives the new surface handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_toroidal(
    XbimContextHandle ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    double majorRadius,
    double minorRadius,
    XbimSurfaceHandle* outHandle);

/*
 * Build a rectangular trimmed surface by restricting a basis surface to
 * a rectangular parameter range [U1, U2] x [V1, V2].
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   basisSurface     – the underlying surface to trim
 *   u1, u2           – parameter bounds in the U direction
 *   v1, v2           – parameter bounds in the V direction
 *   outHandle        – receives the new surface handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_rectangular_trimmed(
    XbimContextHandle ctx,
    XbimSurfaceHandle basisSurface,
    double u1, double u2,
    double v1, double v2,
    XbimSurfaceHandle* outHandle);

/*
 * Build a bounded planar face from a plane placement, an outer boundary wire,
 * and optional inner boundary wires (holes).
 *
 * The boundary wires must be in the plane's local 2D space (z = 0). The
 * resulting face is positioned at the given world placement.
 *
 *   ctx              – a valid context handle (used for logging; may be NULL)
 *   originX/Y/Z      – plane placement origin in world coordinates
 *   zDirX/Y/Z        – plane normal (Z direction of placement)
 *   xDirX/Y/Z        – reference direction (X direction of placement)
 *   outerWire        – shape handle for the outer boundary wire (at z = 0)
 *   innerWires       – array of shape handles for inner holes (may be NULL)
 *   numInnerWires    – number of inner wire handles (0 if no holes)
 *   tolerance        – geometric tolerance for face construction
 *   outHandle        – receives the new face shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_build_curve_bounded_plane(
    XbimContextHandle        ctx,
    double originX, double originY, double originZ,
    double zDirX,   double zDirY,   double zDirZ,
    double xDirX,   double xDirY,   double xDirZ,
    XbimShapeHandle          outerWire,
    const XbimShapeHandle*   innerWires,
    int                      numInnerWires,
    double                   tolerance,
    XbimShapeHandle*         outHandle);

/*
 * Destroy a surface handle and free its resources.
 * Passing NULL is a safe no-op.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_surface_destroy(XbimSurfaceHandle handle);

#pragma endregion

#pragma region Topology Traversal

/*
 * Count the number of sub-shapes of the given type contained in a shape.
 * Uses TopExp_Explorer to iterate over the requested topology level.
 *
 *   handle    – a valid shape handle
 *   subType   – the topology level to count (e.g. XBIM_SHAPE_FACE)
 *   outCount  – receives the count
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if handle is NULL;
 * XBIM_NULL_SHAPE if the shape is null.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_count_subshapes(
    XbimShapeHandle handle,
    XbimShapeType   subType,
    int*            outCount);

/*
 * Extract all sub-shapes of the given type from a shape.
 * The caller must first call xbim_shape_count_subshapes to determine
 * the required array size, then allocate an array of that size.
 *
 * Each returned handle is a new heap-allocated XbimShape_ that the
 * caller owns and must eventually destroy with xbim_shape_destroy.
 *
 *   handle      – a valid shape handle
 *   subType     – the topology level to extract (e.g. XBIM_SHAPE_FACE)
 *   outHandles  – caller-allocated array of at least *count entries
 *   count       – on input: capacity of outHandles array
 *                 on output: actual number of sub-shapes written
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if handle is NULL;
 * XBIM_NULL_SHAPE if the shape is null; XBIM_INVALID_ARG if outHandles
 * or count is NULL, or if the array capacity is too small.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_get_subshapes(
    XbimShapeHandle     handle,
    XbimShapeType       subType,
    XbimShapeHandle*    outHandles,
    int*                count);

/*
 * Get the outer wire (boundary) of a face.
 *
 *   faceHandle  – a shape handle containing a TopoDS_Face
 *   outHandle   – receives the new wire shape handle
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if faceHandle is NULL;
 * XBIM_NULL_SHAPE if the face or outer wire is null.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_outer_wire(
    XbimShapeHandle     faceHandle,
    XbimShapeHandle*    outHandle);

/*
 * Get the inner wires (holes/voids) of a face.
 * The caller must first determine the count (total wires minus 1 for the
 * outer wire), or use xbim_shape_count_subshapes with XBIM_SHAPE_WIRE
 * and subtract 1.
 *
 *   faceHandle  – a shape handle containing a TopoDS_Face
 *   outHandles  – caller-allocated array for inner wire handles
 *   count       – on input: capacity of outHandles array
 *                 on output: actual number of inner wires written
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if faceHandle is NULL;
 * XBIM_NULL_SHAPE if the face is null.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_face_inner_wires(
    XbimShapeHandle     faceHandle,
    XbimShapeHandle*    outHandles,
    int*                count);

#pragma endregion

#pragma region Shape Triangulation

/*
 * Triangulate a shape using BRepMesh_IncrementalMesh.
 * The resulting triangulation is stored on the shape's faces and can be
 * queried with BRep_Tool::Triangulation().
 *
 *   shapeHandle       – the shape to triangulate
 *   linearDeflection  – chord height tolerance in model units
 *   angularDeflection – max angle between adjacent triangle normals (radians)
 *   relative          – 1 to use relative deflection, 0 for absolute
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if shape is NULL;
 * XBIM_NULL_SHAPE if the shape is null; XBIM_ERROR on meshing failure.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_triangulate(
    XbimShapeHandle     shapeHandle,
    double              linearDeflection,
    double              angularDeflection,
    int                 relative);

#pragma endregion

#pragma region WexBim Mesh Creation

/*
 * Triangulate a shape and serialize the result to WexBim binary format.
 * The mesh data is allocated by the native library and must be freed with
 * xbim_buffer_free() when no longer needed.
 *
 *   ctx               – a valid context handle (for logging)
 *   shapeHandle       – the shape to mesh
 *   tolerance         – point coincidence tolerance for vertex deduplication
 *   linearDeflection  – chord height tolerance in model units
 *   angularDeflection – max angle between adjacent triangle normals (radians)
 *   scale             – coordinate scale factor (typically 1/oneMeter)
 *   checkEdges        – 1 to inspect face edges for curves, 0 to skip
 *   outBuffer         – receives pointer to the WexBim byte buffer
 *   outBufferSize     – receives the buffer size in bytes
 *   outHasCurves      – receives 1 if the shape has curved edges, 0 otherwise
 *                        (may be NULL if not needed)
 *   outMinX..outMaxZ  – receives the mesh bounding box in scaled coordinates
 *                        (may be NULL if not needed)
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if ctx or shape is NULL;
 * XBIM_NULL_SHAPE if the shape is null; XBIM_ERROR on meshing failure.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_mesh_create_wexbim(
    XbimContextHandle   ctx,
    XbimShapeHandle     shapeHandle,
    double              tolerance,
    double              linearDeflection,
    double              angularDeflection,
    double              scale,
    int                 checkEdges,
    unsigned char**     outBuffer,
    int*                outBufferSize,
    int*                outHasCurves,
    double*             outMinX, double* outMinY, double* outMinZ,
    double*             outMaxX, double* outMaxY, double* outMaxZ);

/*
 * Compute the mesh bounding box of a shape after triangulation.
 * The bounding box is computed in scaled coordinates (same space as the
 * WexBim mesh vertices).
 *
 *   ctx               – a valid context handle (for logging)
 *   shapeHandle       – the shape to mesh
 *   tolerance         – point coincidence tolerance
 *   linearDeflection  – chord height tolerance
 *   angularDeflection – max angle between normals (radians)
 *   scale             – coordinate scale factor
 *   outMinX..outMaxZ  – receives the bounding box corners
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE/XBIM_NULL_SHAPE on error.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_mesh_get_bounding_box(
    XbimContextHandle   ctx,
    XbimShapeHandle     shapeHandle,
    double              tolerance,
    double              linearDeflection,
    double              angularDeflection,
    double              scale,
    double*             outMinX, double* outMinY, double* outMinZ,
    double*             outMaxX, double* outMaxY, double* outMaxZ);

/*
 * Free a byte buffer that was allocated by the native library.
 * Passing NULL is a safe no-op.
 * Use this to release the buffer returned by xbim_mesh_create_wexbim().
 */
XBIM_EXPORT void XBIM_CALL xbim_buffer_free(unsigned char* buffer);

#pragma endregion

#pragma region BRep Serialization

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

/**
 * Write a shape to a binary STL file.
 *
 *   handle     – a valid shape handle
 *   filePath   – output file path (.stl)
 *   deflection – tessellation chord deviation; smaller = finer mesh (0.1 is typical)
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if handle is NULL;
 * XBIM_NULL_SHAPE if the shape is null; XBIM_ERROR on I/O or OCCT failure.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_write_stl(
    XbimShapeHandle handle,
    const char*     filePath,
    double          deflection);

/*
 * Serialize a shape to an OCCT BRep ASCII string.
 *
 *   handle     – a valid shape handle
 *   outBrepStr – receives a malloc'd null-terminated UTF-8 string
 *   outStrLen  – receives the string length in bytes (excluding null)
 *
 * The caller must free the returned string with xbim_string_free().
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if handle is NULL;
 * XBIM_NULL_SHAPE if the shape is null; XBIM_ERROR on failure.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_to_brep_string(
    XbimShapeHandle handle,
    char**          outBrepStr,
    int*            outStrLen);

/*
 * Deserialize a shape from an OCCT BRep ASCII string.
 *
 *   brepStr – null-terminated BRep ASCII data
 *   strLen  – string length in bytes (or -1 to use strlen)
 *
 * Returns a valid shape handle on success, or NULL on failure.
 * On failure, call xbim_get_last_error() for details.
 */
XBIM_EXPORT XbimShapeHandle XBIM_CALL xbim_shape_from_brep_string(
    const char* brepStr,
    int         strLen);

/*
 * Free a string allocated by native code (e.g. xbim_shape_to_brep_string).
 * Passing NULL is a safe no-op.
 */
XBIM_EXPORT void XBIM_CALL xbim_string_free(char* str);

#pragma endregion

#pragma region Binary Shape Serialization

/*
 * Serialize a shape to OCCT BinTools binary format.
 *
 *   handle        – a valid shape handle
 *   withTriangles – include mesh triangulation data (1 = yes, 0 = no)
 *   withNormals   – include vertex normals (1 = yes, 0 = no)
 *   outBuffer     – receives a malloc'd byte buffer
 *   outSize       – receives the buffer size in bytes
 *
 * The caller must free the returned buffer with xbim_buffer_free().
 *
 * Returns XBIM_OK on success; XBIM_INVALID_HANDLE if handle is NULL;
 * XBIM_NULL_SHAPE if the shape is null; XBIM_ERROR on failure.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_to_binary(
    XbimShapeHandle     handle,
    int                 withTriangles,
    int                 withNormals,
    unsigned char**     outBuffer,
    int*                outSize);

/*
 * Deserialize a shape from OCCT BinTools binary format.
 *
 *   buffer – pointer to the binary data
 *   size   – buffer size in bytes
 *
 * Returns a valid shape handle on success, or NULL on failure.
 * On failure, call xbim_get_last_error() for details.
 */
XBIM_EXPORT XbimShapeHandle XBIM_CALL xbim_shape_from_binary(
    const unsigned char*    buffer,
    int                     size);

#pragma endregion

#pragma region Shape Utilities

/*
 * Merge co-planar faces and co-linear edges using ShapeUpgrade_UnifySameDomain.
 * Returns a new shape with merged geometry.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_shape_unify_domain(
    XbimShapeHandle     shapeHandle,
    XbimShapeHandle*    outHandle);

#pragma endregion

#pragma region Curve2d Lifecycle

/*
 * Destroy a 2D curve handle and free its resources.
 * Passing NULL is a safe no-op.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_destroy(XbimCurve2dHandle handle);

#pragma endregion

#pragma region Curve2d Construction

/*
 * Build a 2D line segment (Geom2d_TrimmedCurve) between two points.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_line(
    XbimContextHandle   ctx,
    double x1, double y1,
    double x2, double y2,
    XbimCurve2dHandle*  outHandle);

/*
 * Build an unbounded 2D line (Geom2d_Line) from origin and direction.
 * Used for IfcLine basis curves where trimming requires an infinite line.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_unbounded_line(
    XbimContextHandle   ctx,
    double originX, double originY,
    double dirX,    double dirY,
    XbimCurve2dHandle*  outHandle);

/*
 * Build a 2D circle (Geom2d_Circle) from center, radius, and reference direction.
 * The reference direction defines the X axis of the circle's local coordinate system.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_circle(
    XbimContextHandle   ctx,
    double cx, double cy,
    double radius,
    double refDirX, double refDirY,
    XbimCurve2dHandle*  outHandle);

/*
 * Build a 2D ellipse (Geom2d_Ellipse) from center, semi-axes, and reference direction.
 * If minorRadius > majorRadius, they are automatically swapped.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_ellipse(
    XbimContextHandle   ctx,
    double cx, double cy,
    double majorRadius, double minorRadius,
    double refDirX, double refDirY,
    XbimCurve2dHandle*  outHandle);

/*
 * Build a generic trimmed 2D curve from a basis curve and parameter range.
 *   sense – nonzero for same-sense, zero for reversed
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_trimmed(
    XbimContextHandle   ctx,
    XbimCurve2dHandle   basisHandle,
    double u1, double u2,
    int sense,
    XbimCurve2dHandle*  outHandle);

/*
 * Build a circular arc (Geom2d_TrimmedCurve) from a circle and parameter range.
 * Uses GCE2d_MakeArcOfCircle. If !sense, parameters are swapped (legacy behavior).
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_arc_of_circle(
    XbimContextHandle   ctx,
    XbimCurve2dHandle   circleHandle,
    double u1, double u2,
    int sense,
    XbimCurve2dHandle*  outHandle);

/*
 * Build an elliptical arc (Geom2d_TrimmedCurve) from an ellipse and parameter range.
 * Uses GCE2d_MakeArcOfEllipse. If !sense, result is reversed (legacy behavior).
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_arc_of_ellipse(
    XbimContextHandle   ctx,
    XbimCurve2dHandle   ellipseHandle,
    double u1, double u2,
    int sense,
    XbimCurve2dHandle*  outHandle);

/*
 * Build a 2D circular arc through three points.
 * Falls back to a line segment if the points are collinear.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_arc_3pt(
    XbimContextHandle   ctx,
    double x1, double y1,
    double x2, double y2,
    double x3, double y3,
    XbimCurve2dHandle*  outHandle);

/*
 * Build a 2D polynomial curve defined by separate X and Y coefficient vectors:
 *   X(u) = sum(coeffsX[i] * u^i), Y(u) = sum(coeffsY[i] * u^i)
 *
 * coeffsX/coeffsY – coefficient arrays (index 0 = constant, index n = highest power)
 * numCoeffsX/numCoeffsY – number of coefficients in each array
 * placementX/Y – origin of the local coordinate system
 * dirX/dirY – reference direction (X axis) of the local coordinate system
 * firstParam/lastParam – parameter domain bounds
 *
 * Returns a 2D curve handle (Geom2d_BoundedCurve).
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_polynomial(
    XbimContextHandle   ctx,
    const double*       coeffsX,
    int                 numCoeffsX,
    const double*       coeffsY,
    int                 numCoeffsY,
    double              placementX,
    double              placementY,
    double              dirX,
    double              dirY,
    double              firstParam,
    double              lastParam,
    XbimCurve2dHandle*  outHandle);

/*
 * Build a 3D polynomial curve as a B-spline approximation.
 * Same parameters as xbim_curve2d_build_polynomial, but evaluates the 2D polynomial
 * curve, samples it, and fits a 3D B-spline in the XY plane (z=0).
 *
 * Returns a 3D curve handle (Geom_BSplineCurve).
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_polynomial(
    XbimContextHandle   ctx,
    const double*       coeffsX,
    int                 numCoeffsX,
    const double*       coeffsY,
    int                 numCoeffsY,
    double              placementX,
    double              placementY,
    double              dirX,
    double              dirY,
    double              firstParam,
    double              lastParam,
    XbimCurveHandle*    outHandle);

#pragma endregion

#pragma region Curve2d Queries

/*
 * Get the parametric range of a 2D curve.
 *   outFirst/outLast – receive the first and last parameter values
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_parameters(
    XbimCurve2dHandle handle,
    double*           outFirst,
    double*           outLast);

/*
 * Compute the arc length of a 2D curve over its full parameter range.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_length(
    XbimCurve2dHandle handle,
    double*           outLength);

/*
 * Evaluate a point on the 2D curve at parameter u.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_value(
    XbimCurve2dHandle handle,
    double            u,
    double*           outX, double* outY);

/*
 * Evaluate point and first derivative at parameter u on a 2D curve.
 *   outPx/outPy – point coordinates
 *   outDx/outDy – first derivative vector
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_d1(
    XbimCurve2dHandle handle,
    double            u,
    double*           outPx, double* outPy,
    double*           outDx, double* outDy);

/*
 * Evaluate point, first derivative, and second derivative at parameter u on a 2D curve.
 *   outPx/outPy     – point coordinates
 *   outD1x/outD1y   – first derivative vector
 *   outD2x/outD2y   – second derivative vector
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_d2(
    XbimCurve2dHandle handle,
    double            u,
    double*           outPx,  double* outPy,
    double*           outD1x, double* outD1y,
    double*           outD2x, double* outD2y);

/*
 * Project a 2D point onto a curve and return the parameter.
 * Uses Geom2dLib_Tool::Parameter.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_project_point(
    XbimContextHandle   ctx,
    XbimCurve2dHandle   curveHandle,
    double px, double py,
    double tolerance,
    double* outParam);

#pragma endregion

#pragma region Curve2d Mutation

/*
 * Reverse the direction of a 2D curve in-place.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_reverse(XbimCurve2dHandle handle);

/*
 * Apply a 2D placement transform to a curve in-place.
 * Transforms from the global origin/X-direction to the given placement.
 * placementX/Y – origin of the local coordinate system
 * dirX/dirY – reference direction (X axis) of the local coordinate system
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_transform(
    XbimCurve2dHandle   handle,
    double              placementX,
    double              placementY,
    double              dirX,
    double              dirY);

/*
 * Translate a 2D curve so that its start point lies at the origin.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_move_to_origin(
    XbimCurve2dHandle   handle);

/*
 * Translate the start point to the origin AND rotate so the starting
 * tangent aligns with the positive X axis.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_align_to_origin(
    XbimCurve2dHandle   handle);

/*
 * Translate a B-spline 2D curve's poles in X so that the start point's
 * X coordinate equals targetX. Used to align the height function's
 * distance-along axis with the horizontal projection.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_translate_start_to_x(
    XbimCurve2dHandle   handle,
    double              targetX);

#pragma endregion

#pragma region Curve2d BSpline

/*
 * Build a 2D B-spline curve from control points, knots, multiplicities, and degree.
 * If weights is non-NULL, builds a rational (NURBS) B-spline.
 *
 * polesXY         – flat array of 2D control points [x0, y0, x1, y1, ...]
 * numPoles        – number of control points (polesXY has numPoles * 2 elements)
 * knots           – distinct knot values
 * numKnots        – number of distinct knots
 * multiplicities  – multiplicity for each knot (parallel to knots, same length)
 * degree          – polynomial degree (>= 1)
 * weights         – per-pole weights for rational B-spline (NULL for non-rational)
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_bspline(
    XbimContextHandle   ctx,
    const double*       polesXY,
    int                 numPoles,
    const double*       knots,
    int                 numKnots,
    const int*          multiplicities,
    int                 degree,
    const double*       weights,
    XbimCurve2dHandle*  outHandle);

#pragma endregion

#pragma region Curve2d Composite

/*
 * Join an array of bounded 2D curves into a single B-spline.
 * Spirals, polynomials, and conics are approximated; other bounded curves are
 * joined via Geom2dConvert_CompCurveToBSplineCurve. Gaps between segments
 * are filled with line segments up to the specified tolerance.
 *
 * curves     – array of XbimCurve2dHandle (each must be a bounded curve)
 * numCurves  – number of curves in the array
 * tolerance  – gap-filling tolerance
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_composite_bspline(
    XbimContextHandle   ctx,
    XbimCurve2dHandle*  curves,
    int                 numCurves,
    double              tolerance,
    XbimCurve2dHandle*  outHandle);

/*
 * Build a 2D offset curve from a basis 2D curve and an offset distance.
 * Uses Geom2d_OffsetCurve(basis, offset).
 *
 *   ctx         – a valid context handle (used for logging; may be NULL)
 *   basisHandle – the 2D basis curve to offset
 *   offset      – offset distance (positive = left of curve direction)
 *   outHandle   – receives the new 2D offset curve handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_offset(
    XbimContextHandle  ctx,
    XbimCurve2dHandle  basisHandle,
    double             offset,
    XbimCurve2dHandle* outHandle);

#pragma endregion

#pragma region Gradient Curve Construction

/*
 * Build a 3D gradient curve from a horizontal 2D projection and a height
 * function (also 2D). The resulting 3D curve evaluates as:
 *   P(u) = (horizontal.X(u), horizontal.Y(u), heightFunction.Y(u))
 *
 * Used for road/railway vertical alignment profiles (IfcGradientCurve).
 * Both input curves must be valid XbimCurve2dHandles.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_gradient(
    XbimContextHandle   ctx,
    XbimCurve2dHandle   horizontalHandle,
    XbimCurve2dHandle   heightFunctionHandle,
    XbimCurveHandle*    outHandle);

/*
 * Build a segmented reference curve from a gradient curve and superelevation
 * segments. Each superelevation segment is a 2D curve paired with a location
 * transform encoding the segment's starting superelevation and cant tilt.
 *
 *   ctx                 – context handle for logging
 *   gradientCurveHandle – base gradient curve (must wrap a Geom_GradientCurve)
 *   segmentCurves       – array of XbimCurve2dHandle for superelevation segments
 *   segmentLocations    – array of XbimLocationHandle for segment placements
 *   numSegments         – number of segments (length of both arrays)
 *   endPointLocation    – optional end point location (NULL for no end point)
 *   outHandle           – receives the new curve handle on success
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_segmented_reference(
    XbimContextHandle   ctx,
    XbimCurveHandle     gradientCurveHandle,
    XbimCurve2dHandle*  segmentCurves,
    XbimLocationHandle* segmentLocations,
    int                 numSegments,
    XbimLocationHandle  endPointLocation,
    XbimCurveHandle*    outHandle);

/*
 * Query superelevation and cant tilt at a given parameter on a segmented
 * reference curve.
 *
 *   curveHandle       – must wrap a Geom_SegmentedReferenceCurve
 *   parameter         – distance-along parameter
 *   outSuperElevation – receives the superelevation value
 *   outCantTilt       – receives the cant tilt angle (radians)
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_get_superelevation_and_tilt(
    XbimCurveHandle curveHandle,
    double          parameter,
    double*         outSuperElevation,
    double*         outCantTilt);

#pragma endregion

#pragma region Spiral Curve Construction

/*
 * Build a clothoid (Euler spiral) as a 3D B-spline approximation.
 * The clothoid is evaluated in the local 2D coordinate system defined by
 * the placement, then fit to a 3D B-spline in the XY plane.
 *
 * clothoidConstant – the A parameter controlling rate of curvature change
 * startParam/endParam – arc length parameter range
 * placementX/Y – origin of the local coordinate system
 * dirX/dirY – reference direction (X axis) of the local coordinate system
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_clothoid(
    XbimContextHandle   ctx,
    double              clothoidConstant,
    double              startParam,
    double              endParam,
    double              placementX,
    double              placementY,
    double              dirX,
    double              dirY,
    XbimCurveHandle*    outHandle);

/*
 * Build a sine spiral as a 3D B-spline approximation.
 * Curvature: kappa(s) = L/C0 + sign(L1)*(L/L1)^2*(s/L) + (L/S)*sin(2*pi*s/L)
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_sine_spiral(
    XbimContextHandle   ctx,
    double              sineTerm,
    double              linearTerm,
    double              constantTerm,
    double              startParam,
    double              endParam,
    double              placementX,
    double              placementY,
    double              dirX,
    double              dirY,
    XbimCurveHandle*    outHandle);

/*
 * Build a cosine spiral as a 3D B-spline approximation.
 * Curvature: kappa(s) = L/C0 + (L/Ct)*cos(pi*s/L)
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_cosine_spiral(
    XbimContextHandle   ctx,
    double              cosineTerm,
    double              constantTerm,
    double              startParam,
    double              endParam,
    double              placementX,
    double              placementY,
    double              dirX,
    double              dirY,
    XbimCurveHandle*    outHandle);

/*
 * Build a polynomial spiral as a 3D B-spline approximation.
 * Supports 2nd, 3rd, and 7th order polynomial spirals via coefficient arrays.
 *
 * coefficients – array of coefficient values (A0 through A7)
 * coefficientPresent – parallel array of flags (nonzero = coefficient is active)
 * numCoefficients – number of entries in both arrays (max 8)
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve_build_polynomial_spiral(
    XbimContextHandle   ctx,
    const double*       coefficients,
    const int*          coefficientPresent,
    int                 numCoefficients,
    double              startParam,
    double              endParam,
    double              placementX,
    double              placementY,
    double              dirX,
    double              dirY,
    XbimCurveHandle*    outHandle);

/*
 * Build a clothoid (Euler spiral) as a 2D curve.
 * Returns the native Geom2d_Clothoid without B-spline conversion,
 * preserving exact evaluation via numerical integration.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_clothoid(
    XbimContextHandle   ctx,
    double              clothoidConstant,
    double              startParam,
    double              endParam,
    double              placementX,
    double              placementY,
    double              dirX,
    double              dirY,
    XbimCurve2dHandle*  outHandle);

/*
 * Build a sine spiral as a 2D curve.
 * Returns the native Geom2d_SineSpiral without B-spline conversion.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_sine_spiral(
    XbimContextHandle   ctx,
    double              sineTerm,
    double              linearTerm,
    double              constantTerm,
    double              startParam,
    double              endParam,
    double              placementX,
    double              placementY,
    double              dirX,
    double              dirY,
    XbimCurve2dHandle*  outHandle);

/*
 * Build a cosine spiral as a 2D curve.
 * Returns the native Geom2d_CosineSpiral without B-spline conversion.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_cosine_spiral(
    XbimContextHandle   ctx,
    double              cosineTerm,
    double              constantTerm,
    double              startParam,
    double              endParam,
    double              placementX,
    double              placementY,
    double              dirX,
    double              dirY,
    XbimCurve2dHandle*  outHandle);

/*
 * Build a polynomial spiral as a 2D curve.
 * Returns the native Geom2d_PolynomialSpiral without B-spline conversion.
 *
 * coefficients – array of coefficient values (A0 through A7)
 * coefficientPresent – parallel array of flags (nonzero = coefficient is active)
 * numCoefficients – number of entries in both arrays (max 8)
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_curve2d_build_polynomial_spiral(
    XbimContextHandle   ctx,
    const double*       coefficients,
    const int*          coefficientPresent,
    int                 numCoefficients,
    double              startParam,
    double              endParam,
    double              placementX,
    double              placementY,
    double              dirX,
    double              dirY,
    XbimCurve2dHandle*  outHandle);

#pragma endregion

#pragma region Wire from 2D Curves

/*
 * Build a wire from an array of 2D bounded curves.
 * Edges are created via BRepBuilderAPI_MakeEdge2d, vertices are shared between
 * consecutive edges, 3D curves are generated via BRepLib::BuildCurve3d,
 * and the wire is closed if the first and last points are within gapSize.
 *
 * This matches the legacy NWireFactory::BuildWire(TColGeom2d_SequenceOfBoundedCurve)
 * approach for building profile wires from 2D curve segments.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_wire_build_from_2d_curves(
    XbimContextHandle   ctx,
    XbimCurve2dHandle*  curves,
    int                 numCurves,
    double              tolerance,
    double              gapSize,
    XbimShapeHandle*    outWire);

#pragma endregion

#pragma region Advanced BRep Builder

/*
 * Create an advanced BRep builder that accumulates IFC face data and builds
 * the full shell topology natively with proper vertex/edge sharing.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_create(
    XbimContextHandle ctx,
    double tolerance,
    XbimAdvancedBrepBuilderHandle* outBuilder);

/*
 * Destroy an advanced BRep builder, releasing all internal state.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_destroy(
    XbimAdvancedBrepBuilderHandle builder);

/*
 * Pre-register a vertex by IFC entity label and coordinates.
 * Vertices with the same label are shared across edges.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_add_vertex(
    XbimAdvancedBrepBuilderHandle builder,
    int vertexLabel,
    double x, double y, double z);

/*
 * Register an edge curve geometry by IFC entity label.
 * The curve is used when building edges during the build phase.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_add_edge_curve(
    XbimAdvancedBrepBuilderHandle builder,
    int edgeLabel,
    XbimCurveHandle curveHandle);

/*
 * Begin a new face with the given surface and sameSense flag.
 * Must be followed by bound/edge calls and ended with end_face.
 * buildRuledSurface: nonzero if the face surface is IIfcSurfaceOfLinearExtrusion,
 * enabling ruled-surface fallback when the IFC surface doesn't match the wire geometry.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_begin_face(
    XbimAdvancedBrepBuilderHandle builder,
    XbimSurfaceHandle surfaceHandle,
    int sameSense,
    int buildRuledSurface);

/*
 * Begin a new bound (wire loop) within the current face.
 * isOuter: nonzero if this is the outer bound.
 * orientation: nonzero if the bound has positive orientation.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_begin_bound(
    XbimAdvancedBrepBuilderHandle builder,
    int isOuter,
    int orientation);

/*
 * Add an oriented edge to the current bound.
 * edgeLabel matches a previously registered edge curve.
 * startVertexLabel/endVertexLabel match registered vertices.
 * sameSense: nonzero if the edge traversal matches its curve direction.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_add_bound_edge(
    XbimAdvancedBrepBuilderHandle builder,
    int edgeLabel,
    int startVertexLabel,
    int endVertexLabel,
    int sameSense);

/*
 * End the current bound (wire loop).
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_end_bound(
    XbimAdvancedBrepBuilderHandle builder);

/*
 * End the current face.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_end_face(
    XbimAdvancedBrepBuilderHandle builder);

/*
 * Build the advanced BRep shell from all accumulated data and return
 * it as a solid shape. Performs vertex/edge deduplication, wire
 * construction, face building, shell assembly and shell-to-solid
 * conversion with orientation checking.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_advanced_brep_build(
    XbimAdvancedBrepBuilderHandle builder,
    XbimShapeHandle* outHandle);

#pragma endregion

#pragma region Grid Operations

/*
 * Create a visual representation of an IFC grid by sweeping a small
 * rectangular cross-section (75mm x 1mm) along each grid axis curve.
 *
 * The caller builds 2D curves from the IFC grid's U, V, and W axes
 * and passes them as three arrays. The function computes curve
 * intersections to determine the grid extent, trims unbounded lines
 * to that extent, converts each curve to 3D, and sweeps a rectangular
 * profile along each to produce a compound of solids.
 *
 *   ctx            - a valid context handle (used for logging; may be NULL)
 *   uCurves        - array of 2D curve handles for U-axis curves
 *   uCount         - number of U-axis curves
 *   vCurves        - array of 2D curve handles for V-axis curves
 *   vCount         - number of V-axis curves
 *   wCurves        - array of 2D curve handles for W-axis curves
 *   wCount         - number of W-axis curves
 *   precision      - model precision tolerance
 *   oneMillimeter  - length of one millimetre in model units
 *   outHandle      - receives the compound shape handle
 *
 * Returns XBIM_OK on success.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_grid_create(
    XbimContextHandle        ctx,
    const XbimCurve2dHandle* uCurves, int uCount,
    const XbimCurve2dHandle* vCurves, int vCount,
    const XbimCurve2dHandle* wCurves, int wCount,
    double                   precision,
    double                   oneMillimeter,
    XbimShapeHandle*         outHandle);

#pragma endregion

#ifdef __cplusplus
}
#endif

#endif /* XBIM_GEOMETRY_API_H */
