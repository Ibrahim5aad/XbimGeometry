/*
 * xbim_compound.cpp
 *
 * Implements compound shape operations via the flat C API:
 *   - Make: assemble multiple shapes into a TopoDS_Compound
 *   - Sew: sew shape faces together using BRepBuilderAPI_Sewing
 *   - Cut: boolean cut a tool shape from a compound
 */

#include "xbim_compound.h"
#include "xbim_shape.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <TopoDS.hxx>
#include <TopoDS_Shape.hxx>
#include <TopoDS_Compound.hxx>
#include <TopoDS_Iterator.hxx>
#include <BRep_Builder.hxx>
#include <BRepBuilderAPI_Sewing.hxx>
#include <BRepAlgoAPI_BooleanOperation.hxx>
#include <BOPAlgo_PaveFiller.hxx>
#include <BOPAlgo_Alerts.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <ShapeFix_Shape.hxx>
#include <TopTools_ListOfShape.hxx>
#include <Standard_Failure.hxx>
#include <Precision.hxx>

#include <sstream>

/* ── Internal helpers ─────────────────────────────────────────────────────── */

static bool is_empty(const TopoDS_Shape& shape)
{
    return shape.IsNull() || shape.NbChildren() == 0;
}

static TopoDS_Shape trim_topology(const TopoDS_Shape& shape)
{
    if (shape.ShapeType() != TopAbs_COMPOUND || shape.NbChildren() != 1)
        return shape;
    TopoDS_Iterator it(shape);
    return trim_topology(it.Value());
}

/* ── Exported C API ───────────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_compound_make(
    XbimContextHandle         ctx,
    const XbimShapeHandle*    shapeHandles,
    int                       numShapes,
    XbimShapeHandle*          outHandle)
{
    xbim_clear_error();
    if (!outHandle) { xbim_set_error("outHandle is NULL"); return XBIM_INVALID_ARG; }
    *outHandle = nullptr;
    if (!shapeHandles) { xbim_set_error("shapeHandles is NULL"); return XBIM_INVALID_ARG; }
    if (numShapes < 0) { xbim_set_error("numShapes must be >= 0"); return XBIM_INVALID_ARG; }

    try
    {
        BRep_Builder builder;
        TopoDS_Compound compound;
        builder.MakeCompound(compound);

        for (int i = 0; i < numShapes; i++)
        {
            if (!shapeHandles[i])
            {
                xbim_log_warning(ctx, "Skipping NULL shape handle at index %d in compound make", i);
                continue;
            }
            const TopoDS_Shape& child = shapeHandles[i]->shape;
            if (!child.IsNull())
                builder.Add(compound, child);
        }

        *outHandle = xbim_shape_create_from(compound);
        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "Failed to make compound");
        xbim_set_error("Failed to make compound");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_compound_sew(
    XbimContextHandle         ctx,
    const XbimShapeHandle*    shapeHandles,
    int                       numShapes,
    double                    tolerance,
    XbimShapeHandle*          outHandle)
{
    xbim_clear_error();
    if (!outHandle) { xbim_set_error("outHandle is NULL"); return XBIM_INVALID_ARG; }
    *outHandle = nullptr;
    if (!shapeHandles) { xbim_set_error("shapeHandles is NULL"); return XBIM_INVALID_ARG; }
    if (numShapes < 1) { xbim_set_error("numShapes must be >= 1"); return XBIM_INVALID_ARG; }
    if (tolerance <= 0.0) { xbim_set_error("tolerance must be > 0"); return XBIM_INVALID_ARG; }

    try
    {
        BRepBuilderAPI_Sewing sewer(tolerance);

        for (int i = 0; i < numShapes; i++)
        {
            if (!shapeHandles[i])
            {
                xbim_log_warning(ctx, "Skipping NULL shape handle at index %d in compound sew", i);
                continue;
            }
            const TopoDS_Shape& child = shapeHandles[i]->shape;
            if (!child.IsNull())
                sewer.Add(child);
        }

        sewer.Perform();
        TopoDS_Shape result = sewer.SewedShape();

        if (result.IsNull())
        {
            xbim_set_error("Compound sew produced a null shape");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(result);
        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "Failed to sew compound");
        xbim_set_error("Failed to sew compound");
        return XBIM_ERROR;
    }
}

XBIM_EXPORT XbimResult XBIM_CALL xbim_compound_cut(
    XbimContextHandle   ctx,
    XbimShapeHandle     compoundHandle,
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
    if (!compoundHandle) { xbim_set_error("compoundHandle is NULL"); return XBIM_INVALID_HANDLE; }
    if (!toolHandle) { xbim_set_error("toolHandle is NULL"); return XBIM_INVALID_HANDLE; }

    const auto& compound = compoundHandle->shape;
    const auto& tool = toolHandle->shape;

    if (is_empty(compound))
    {
        xbim_log_warning(ctx, "Attempt to cut from an empty compound. Result is an empty shape.");
        *outHasWarnings = 1;
        return XBIM_NULL_SHAPE;
    }
    if (is_empty(tool))
    {
        xbim_log_warning(ctx, "Attempt to cut with an empty tool. Result is the compound unchanged.");
        *outHasWarnings = 1;
        *outHandle = xbim_shape_create_from(compound);
        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }

    try
    {
        TopTools_ListOfShape arguments;
        TopTools_ListOfShape tools;
        arguments.Append(compound);
        tools.Append(tool);

        BRepAlgoAPI_BooleanOperation bop;
        bop.SetArguments(arguments);
        bop.SetTools(tools);
        bop.SetOperation(BOPAlgo_CUT);
        bop.SetRunParallel(false);
        bop.SetNonDestructive(true);
        bop.SetFuzzyValue(fuzzyTolerance);
        bop.Build();

        if (bop.HasErrors())
        {
            std::ostringstream msg;
            auto& report = bop.GetReport();
            report->Dump(msg);
            xbim_log_error(ctx, "Compound cut failed: %s", msg.str().c_str());
            xbim_set_error("Compound boolean cut failed");
            return XBIM_ERROR;
        }

        if (!bop.IsDone())
        {
            xbim_set_error("Compound boolean cut did not complete");
            return XBIM_ERROR;
        }

        TopoDS_Shape result = bop.Shape();
        BRepCheck_Analyzer analyzer(result);
        if (!analyzer.IsValid())
        {
            xbim_log_warning(ctx, "Compound cut result is invalid, skipping SimplifyResult().");
            *outHasWarnings = 1;
        }
        else
        {
            bop.SimplifyResult(true, true, Precision::Angular());
        }

        // Check for self-intersection warning
        if (bop.DSFiller()->HasWarning(STANDARD_TYPE(BOPAlgo_AlertAcquiredSelfIntersection)))
        {
            xbim_log_warning(ctx, "Compound cut result has acquired self-intersection.");
            *outHasWarnings = 1;

            // Attempt to fix
            ShapeFix_Shape fixer(result);
            if (fixer.Perform())
            {
                result = fixer.Shape();
                xbim_log_debug(ctx, "Self-intersection in compound cut result has been fixed.");
            }
        }

        result = trim_topology(result);

        if (result.IsNull())
        {
            xbim_set_error("Compound cut produced a null shape");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(result);
        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "Compound cut failed");
        xbim_set_error("Compound cut failed");
        return XBIM_ERROR;
    }
}
