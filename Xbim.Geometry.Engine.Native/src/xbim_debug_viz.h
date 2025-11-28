/*
 * xbim_debug_viz.h
 *
 * Debug-only shape visualization using OCCT's V3d viewer.
 * Compiled only when XBIM_DEBUG_VIZ is defined (Debug builds).
 *
 * Not part of the release API — used during development only.
 */

#ifndef XBIM_DEBUG_VIZ_H
#define XBIM_DEBUG_VIZ_H

#include "xbim_geometry_api.h"

#ifdef XBIM_DEBUG_VIZ

#ifdef __cplusplus
extern "C" {
#endif

/*
 * Show a shape in an interactive 3D viewer window.
 * Blocks until the viewer window is closed.
 *
 * Controls:
 *   Left drag   = rotate
 *   Right drag  = pan
 *   Mouse wheel = zoom
 *   F           = fit all
 *   W           = wireframe mode
 *   S           = shaded mode
 *   T           = top view
 *   Escape      = close
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_debug_view_shape(XbimShapeHandle shape);

/*
 * Show multiple shapes in a single viewer, each with an optional RGB color.
 *
 * shapes:  array of shape handles (count elements)
 * count:   number of shapes
 * r, g, b: optional color arrays (count elements each, range 0.0–1.0).
 *          Pass NULL for all three to use a built-in color palette.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_debug_view_shapes(
    XbimShapeHandle* shapes, int count,
    const double* r, const double* g, const double* b);

/*
 * Write a shape to a BREP file for external inspection.
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_debug_dump_brep(
    XbimShapeHandle shape, const char* filepath);

/*
 * Write a shape to a binary STL file (tessellated mesh).
 * deflection: mesh quality — smaller = finer (0.1 is a reasonable default).
 */
XBIM_EXPORT XbimResult XBIM_CALL xbim_debug_dump_stl(
    XbimShapeHandle shape, const char* filepath, double deflection);

#ifdef __cplusplus
}
#endif

#endif /* XBIM_DEBUG_VIZ */
#endif /* XBIM_DEBUG_VIZ_H */
