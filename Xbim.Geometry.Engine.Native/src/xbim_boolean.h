/*
 * xbim_boolean.h
 *
 * Internal header for boolean operations.
 * Not part of the public C API - used by native implementation code only.
 *
 * Declares shared helpers (is_empty, trim_topology, perform_boolean)
 * used by both xbim_boolean.cpp and xbim_compound.cpp.
 */

#ifndef XBIM_BOOLEAN_H
#define XBIM_BOOLEAN_H

#include "xbim_geometry_api.h"

#ifdef __cplusplus

#include <TopoDS_Shape.hxx>
#include <TopTools_ListOfShape.hxx>
#include <BOPAlgo_Operation.hxx>

struct XbimContext_;

/* Returns true if shape is null or has no children. */
bool is_empty(const TopoDS_Shape& shape);

/* Unwraps single-child compounds recursively to their highest-level topology. */
TopoDS_Shape trim_topology(const TopoDS_Shape& shape);

/*
 * Core boolean operation using BRepAlgoAPI_BooleanOperation.
 * Handles error reporting, result validation, SimplifyResult,
 * and self-intersection detection with automatic shape fixing (one retry).
 * Returns an empty shape on failure.
 */
TopoDS_Shape perform_boolean(
    const XbimContext_* ctx,
    const TopTools_ListOfShape& arguments,
    const TopTools_ListOfShape& tools,
    double fuzzyTolerance,
    BOPAlgo_Operation operation,
    int& hasWarnings,
    bool attemptingFix = false);

#endif /* __cplusplus */

#endif /* XBIM_BOOLEAN_H */
