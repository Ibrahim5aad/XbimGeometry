/*
 * xbim_boolean.cpp
 *
 * Implements boolean operations (union, cut, intersect) via the flat C API.
 * Ports NBooleanFactory from the C++/CLI engine:
 *   - Union (BOPAlgo_FUSE)
 *   - Cut (BOPAlgo_CUT)
 *   - Intersect (BOPAlgo_COMMON)
 *
 * Includes self-intersection detection and automatic shape fixing,
 * topology trimming, and result simplification.
 */

#include "xbim_boolean.h"
#include "xbim_shape.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <sstream>

#include <TopoDS.hxx>
#include <TopoDS_Shape.hxx>
#include <TopoDS_Iterator.hxx>
#include <TopAbs_ShapeEnum.hxx>
#include <TopTools_ListOfShape.hxx>
#include <BRepAlgoAPI_BooleanOperation.hxx>
#include <BOPAlgo_PaveFiller.hxx>
#include <BOPAlgo_Alerts.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <ShapeFix_Shape.hxx>
#include <Standard_Failure.hxx>
#include <Standard_Type.hxx>
#include <Precision.hxx>

/* ── Internal helpers ─────────────────────────────────────────────────────── */

static bool is_empty(const TopoDS_Shape& shape)
{
    return shape.IsNull() || shape.NbChildren() == 0;
}

/*
 * Reduces a compound to its highest-level topology. If a compound
 * contains only one child, returns that child recursively.
 */
static TopoDS_Shape trim_topology(const TopoDS_Shape& shape)
{
    if (shape.ShapeType() != TopAbs_COMPOUND || shape.NbChildren() != 1)
        return shape;
    TopoDS_Iterator it(shape);
    return trim_topology(it.Value());
}

/*
 * Core boolean operation using BRepAlgoAPI_BooleanOperation.
 * Handles:
 *   - Error reporting via OCCT GetReport
 *   - Result validation and SimplifyResult
 *   - Self-intersection detection with automatic shape fixing (one retry)
 */
static TopoDS_Shape perform_boolean(
    const XbimContext_* ctx,
    const TopTools_ListOfShape& arguments,
    const TopTools_ListOfShape& tools,
    double fuzzyTolerance,
    BOPAlgo_Operation operation,
    int& hasWarnings,
    bool attemptingFix)
{
    try
    {
        hasWarnings = 0;
        BRepAlgoAPI_BooleanOperation bop;
        bop.SetArguments(arguments);
        bop.SetTools(tools);
        bop.SetOperation(operation);
        bop.SetRunParallel(false);
        bop.SetNonDestructive(true);
        bop.SetFuzzyValue(fuzzyTolerance);
        bop.Build();

        if (bop.HasErrors())
        {
            std::ostringstream msg;
            auto& report = bop.GetReport();
            report->Dump(msg);
            Standard_Failure::Raise(msg.str().c_str());
        }

        if (bop.IsDone())
        {
            TopoDS_Shape result = bop.Shape();
            BRepCheck_Analyzer analyzer(result);
            if (!analyzer.IsValid())
            {
                xbim_log_warning(ctx, "Boolean resulting shape is invalid, skipping SimplifyResult().");
            }
            else
            {
                bop.SimplifyResult(true, true, Precision::Angular());
            }

            // Detect self-intersection acquired during boolean — fix shapes and retry once
            if (bop.DSFiller()->HasWarning(STANDARD_TYPE(BOPAlgo_AlertAcquiredSelfIntersection))
                && !attemptingFix)
            {
                TopTools_ListOfShape fixedArguments;
                TopTools_ListOfShape fixedTools;
                bool fixPossible = false;

                for (auto it = arguments.cbegin(); it != arguments.cend(); ++it)
                {
                    ShapeFix_Shape shapeFixer(*it);
                    if (shapeFixer.Perform())
                    {
                        fixedArguments.Append(shapeFixer.Shape());
                        fixPossible = true;
                    }
                    else
                    {
                        fixedArguments.Append(*it);
                    }
                }
                for (auto it = tools.cbegin(); it != tools.cend(); ++it)
                {
                    ShapeFix_Shape shapeFixer(*it);
                    if (shapeFixer.Perform())
                    {
                        fixedTools.Append(shapeFixer.Shape());
                        fixPossible = true;
                    }
                    else
                    {
                        fixedTools.Append(*it);
                    }
                }

                if (fixPossible)
                    return perform_boolean(ctx, fixedArguments, fixedTools,
                                           fuzzyTolerance, operation, hasWarnings, true);
            }

            if (attemptingFix)
                xbim_log_debug(ctx, "Self-intersection of sub-shapes in Boolean output has been fixed.");

            return trim_topology(bop.Shape());
        }
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "Boolean operation failed");
    }

    xbim_log_error(ctx, "Failed to perform boolean operation");
    return TopoDS_Shape(); // empty shape signals failure
}

/* ── Exported C API ───────────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_boolean_union(
    XbimContextHandle   ctx,
    XbimShapeHandle     bodyHandle,
    XbimShapeHandle     toolHandle,
    double              fuzzyTolerance,
    int*                outHasWarnings,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();
    if (!outHandle) { xbim_set_error("outHandle is NULL"); return XBIM_INVALID_ARG; }
    *outHandle = nullptr;
    if (!outHasWarnings) { xbim_set_error("outHasWarnings is NULL"); return XBIM_INVALID_ARG; }
    *outHasWarnings = 0;
    if (!bodyHandle) { xbim_set_error("bodyHandle is NULL"); return XBIM_INVALID_HANDLE; }
    if (!toolHandle) { xbim_set_error("toolHandle is NULL"); return XBIM_INVALID_HANDLE; }

    const auto& body = bodyHandle->shape;
    const auto& tool = toolHandle->shape;

    // Handle empty shape edge cases
    if (is_empty(body) && is_empty(tool))
    {
        xbim_log_warning(ctx, "Attempt to Union two empty shapes. Result is an empty shape.");
        *outHasWarnings = 1;
        return XBIM_NULL_SHAPE;
    }
    if (is_empty(tool))
    {
        xbim_log_warning(ctx, "Attempt to Union two shapes, the tool is empty. Result is the body.");
        *outHasWarnings = 1;
        *outHandle = xbim_shape_create_from(body);
        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }
    if (is_empty(body))
    {
        xbim_log_warning(ctx, "Attempt to Union two shapes, the body is empty. Result is the tool.");
        *outHasWarnings = 1;
        *outHandle = xbim_shape_create_from(tool);
        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }

    TopTools_ListOfShape arguments;
    TopTools_ListOfShape tools;
    arguments.Append(body);
    tools.Append(tool);

    int hasWarnings = 0;
    TopoDS_Shape result = perform_boolean(ctx, arguments, tools,
                                           fuzzyTolerance, BOPAlgo_FUSE, hasWarnings, false);
    *outHasWarnings = hasWarnings;

    if (result.IsNull())
    {
        xbim_set_error("Boolean union produced a null shape");
        return XBIM_NULL_SHAPE;
    }

    *outHandle = xbim_shape_create_from(result);
    return *outHandle ? XBIM_OK : XBIM_ERROR;
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_boolean_cut(
    XbimContextHandle   ctx,
    XbimShapeHandle     bodyHandle,
    XbimShapeHandle     toolHandle,
    double              fuzzyTolerance,
    int*                outHasWarnings,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();
    if (!outHandle) { xbim_set_error("outHandle is NULL"); return XBIM_INVALID_ARG; }
    *outHandle = nullptr;
    if (!outHasWarnings) { xbim_set_error("outHasWarnings is NULL"); return XBIM_INVALID_ARG; }
    *outHasWarnings = 0;
    if (!bodyHandle) { xbim_set_error("bodyHandle is NULL"); return XBIM_INVALID_HANDLE; }
    if (!toolHandle) { xbim_set_error("toolHandle is NULL"); return XBIM_INVALID_HANDLE; }

    const auto& body = bodyHandle->shape;
    const auto& tool = toolHandle->shape;

    // Handle empty shape edge cases
    if (is_empty(body) && is_empty(tool))
    {
        xbim_log_warning(ctx, "Attempt to Cut two empty shapes. Result is an empty shape.");
        *outHasWarnings = 1;
        return XBIM_NULL_SHAPE;
    }
    if (is_empty(tool))
    {
        xbim_log_warning(ctx, "Attempt to Cut shapes, the tool is empty. Result is the body.");
        *outHasWarnings = 1;
        *outHandle = xbim_shape_create_from(body);
        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }
    if (is_empty(body))
    {
        xbim_log_warning(ctx, "Attempt to Cut shapes, the body is empty. Result is an empty shape.");
        *outHasWarnings = 1;
        return XBIM_NULL_SHAPE;
    }

    TopTools_ListOfShape arguments;
    TopTools_ListOfShape tools;
    arguments.Append(body);
    tools.Append(tool);

    int hasWarnings = 0;
    TopoDS_Shape result = perform_boolean(ctx, arguments, tools,
                                           fuzzyTolerance, BOPAlgo_CUT, hasWarnings, false);
    *outHasWarnings = hasWarnings;

    if (result.IsNull())
    {
        xbim_set_error("Boolean cut produced a null shape");
        return XBIM_NULL_SHAPE;
    }

    *outHandle = xbim_shape_create_from(result);
    return *outHandle ? XBIM_OK : XBIM_ERROR;
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_boolean_intersect(
    XbimContextHandle   ctx,
    XbimShapeHandle     bodyHandle,
    XbimShapeHandle     toolHandle,
    double              fuzzyTolerance,
    int*                outHasWarnings,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();
    if (!outHandle) { xbim_set_error("outHandle is NULL"); return XBIM_INVALID_ARG; }
    *outHandle = nullptr;
    if (!outHasWarnings) { xbim_set_error("outHasWarnings is NULL"); return XBIM_INVALID_ARG; }
    *outHasWarnings = 0;
    if (!bodyHandle) { xbim_set_error("bodyHandle is NULL"); return XBIM_INVALID_HANDLE; }
    if (!toolHandle) { xbim_set_error("toolHandle is NULL"); return XBIM_INVALID_HANDLE; }

    const auto& body = bodyHandle->shape;
    const auto& tool = toolHandle->shape;

    // For intersection, if either is empty there can be no intersection
    if (is_empty(body) || is_empty(tool))
    {
        xbim_log_warning(ctx, "Attempt to Intersect one or more empty shapes. Result is an empty shape.");
        *outHasWarnings = 1;
        return XBIM_NULL_SHAPE;
    }

    TopTools_ListOfShape arguments;
    TopTools_ListOfShape tools;
    arguments.Append(body);
    tools.Append(tool);

    int hasWarnings = 0;
    TopoDS_Shape result = perform_boolean(ctx, arguments, tools,
                                           fuzzyTolerance, BOPAlgo_COMMON, hasWarnings, false);
    *outHasWarnings = hasWarnings;

    if (result.IsNull())
    {
        xbim_set_error("Boolean intersect produced a null shape");
        return XBIM_NULL_SHAPE;
    }

    *outHandle = xbim_shape_create_from(result);
    return *outHandle ? XBIM_OK : XBIM_ERROR;
}
