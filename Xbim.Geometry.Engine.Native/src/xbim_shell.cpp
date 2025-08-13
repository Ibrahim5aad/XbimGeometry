/*
 * xbim_shell.cpp
 *
 * Implements shell construction and repair operations via the flat C API:
 *   - Build a shell from an array of face shapes
 *   - Sew/fix a shell using ShapeFix_Shell with orientation checking
 *   - Convert a closed shell into a solid
 *
 * Ports NShellFactory methods from the C++/CLI engine.
 */

#include "xbim_shell.h"
#include "xbim_shape.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <TopoDS.hxx>
#include <TopoDS_Shell.hxx>
#include <TopoDS_Solid.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Shape.hxx>
#include <BRep_Builder.hxx>
#include <BRepBuilderAPI_MakeSolid.hxx>
#include <BRepCheck_Shell.hxx>
#include <BRepOffsetAPI_Sewing.hxx>
#include <ShapeFix_Shape.hxx>
#include <ShapeFix_Shell.hxx>
#include <ShapeFix_Solid.hxx>
#include <TopExp_Explorer.hxx>
#include <TopAbs_ShapeEnum.hxx>
#include <Standard_Failure.hxx>

/* ── xbim_shell_build_from_faces ───────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_shell_build_from_faces(
    XbimContextHandle        ctx,
    const XbimShapeHandle*   faceHandles,
    int                      numFaces,
    double                   tolerance,
    XbimShapeHandle*         outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_shell_build_from_faces: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!faceHandles)
    {
        xbim_set_error("xbim_shell_build_from_faces: faceHandles is NULL");
        return XBIM_INVALID_ARG;
    }
    if (numFaces < 1)
    {
        xbim_set_error("xbim_shell_build_from_faces: numFaces must be >= 1");
        return XBIM_INVALID_ARG;
    }

    try
    {
        BRep_Builder builder;
        TopoDS_Shell shell;
        builder.MakeShell(shell);

        int addedCount = 0;
        for (int i = 0; i < numFaces; i++)
        {
            if (!faceHandles[i])
            {
                xbim_log_warning(ctx, "Skipping NULL face handle at index %d in shell build", i);
                continue;
            }

            const TopoDS_Shape& shape = faceHandles[i]->shape;
            if (shape.IsNull())
            {
                xbim_log_warning(ctx, "Skipping null shape at index %d in shell build", i);
                continue;
            }

            if (shape.ShapeType() == TopAbs_FACE)
            {
                builder.Add(shell, TopoDS::Face(shape));
                addedCount++;
            }
            else
            {
                /* If not a face, try to extract faces from the shape */
                for (TopExp_Explorer exp(shape, TopAbs_FACE); exp.More(); exp.Next())
                {
                    builder.Add(shell, TopoDS::Face(exp.Current()));
                    addedCount++;
                }
            }
        }

        if (addedCount == 0)
        {
            xbim_set_error("xbim_shell_build_from_faces: no valid faces were added");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(shell);
        if (!*outHandle)
        {
            xbim_set_error("xbim_shell_build_from_faces: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_shell_build_from_faces");
        xbim_set_error("xbim_shell_build_from_faces: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── xbim_shell_sew ────────────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_shell_sew(
    XbimContextHandle   ctx,
    XbimShapeHandle     shellHandle,
    double              tolerance,
    int*                outIsFixed,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_shell_sew: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!outIsFixed)
    {
        xbim_set_error("xbim_shell_sew: outIsFixed is NULL");
        return XBIM_INVALID_ARG;
    }
    *outIsFixed = 0;

    if (!shellHandle)
    {
        xbim_set_error("xbim_shell_sew: shellHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = shellHandle->shape;
        if (shape.IsNull())
        {
            xbim_set_error("xbim_shell_sew: shape is null");
            return XBIM_NULL_SHAPE;
        }

        /* Extract a shell from the input - it might be a shell directly,
         * or contain a shell as a sub-shape */
        TopoDS_Shell shell;
        if (shape.ShapeType() == TopAbs_SHELL)
        {
            shell = TopoDS::Shell(shape);
        }
        else
        {
            /* Try to find a shell inside the shape */
            TopExp_Explorer exp(shape, TopAbs_SHELL);
            if (exp.More())
                shell = TopoDS::Shell(exp.Current());
        }

        if (shell.IsNull())
        {
            xbim_set_error("xbim_shell_sew: input does not contain a shell");
            return XBIM_INVALID_ARG;
        }

        /* Check orientation first - if already OK, return as-is */
        BRepCheck_Shell checker(shell);
        BRepCheck_Status shellStatus = checker.Orientation();

        if (shellStatus == BRepCheck_NoError)
        {
            *outIsFixed = 1;
            *outHandle = xbim_shape_create_from(shell);
            return *outHandle ? XBIM_OK : XBIM_ERROR;
        }

        /* Attempt to fix the shell */
        ShapeFix_Shell shapeFixer(shell);
        shapeFixer.SetPrecision(tolerance);
        bool fixed = shapeFixer.Perform();

        if (!fixed)
        {
            xbim_log_warning(ctx, "ShapeFix_Shell could not repair shell orientation");
            *outHandle = xbim_shape_create_from(shell);
            return *outHandle ? XBIM_OK : XBIM_ERROR;
        }

        TopoDS_Shape result = shapeFixer.Shape();
        if (result.IsNull())
        {
            xbim_log_warning(ctx, "ShapeFix_Shell produced a null shape, returning original");
            *outHandle = xbim_shape_create_from(shell);
            return *outHandle ? XBIM_OK : XBIM_ERROR;
        }

        if (result.ShapeType() == TopAbs_SHELL)
        {
            *outIsFixed = 1;
            *outHandle = xbim_shape_create_from(shapeFixer.Shell());
        }
        else if (result.ShapeType() == TopAbs_COMPOUND)
        {
            *outIsFixed = 1;
            *outHandle = xbim_shape_create_from(result);
        }
        else
        {
            /* Unknown result type — return original shell, not fixed */
            *outHandle = xbim_shape_create_from(shell);
        }

        return *outHandle ? XBIM_OK : XBIM_ERROR;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_shell_sew");
        xbim_set_error("xbim_shell_sew: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── xbim_shell_make_solid ─────────────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_shell_make_solid(
    XbimContextHandle   ctx,
    XbimShapeHandle     shellHandle,
    XbimShapeHandle*    outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_shell_make_solid: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!shellHandle)
    {
        xbim_set_error("xbim_shell_make_solid: shellHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = shellHandle->shape;
        if (shape.IsNull())
        {
            xbim_set_error("xbim_shell_make_solid: shape is null");
            return XBIM_NULL_SHAPE;
        }

        /* Extract shell from the input */
        TopoDS_Shell shell;
        if (shape.ShapeType() == TopAbs_SHELL)
        {
            shell = TopoDS::Shell(shape);
        }
        else
        {
            TopExp_Explorer exp(shape, TopAbs_SHELL);
            if (exp.More())
                shell = TopoDS::Shell(exp.Current());
        }

        if (shell.IsNull())
        {
            xbim_set_error("xbim_shell_make_solid: input does not contain a shell");
            return XBIM_INVALID_ARG;
        }

        BRepBuilderAPI_MakeSolid solidMaker(shell);
        if (!solidMaker.IsDone())
        {
            xbim_set_error("xbim_shell_make_solid: could not create solid from shell");
            xbim_log_warning(ctx, "Failed to convert shell to solid");
            return XBIM_NULL_SHAPE;
        }

        TopoDS_Solid solid = solidMaker.Solid();
        if (solid.IsNull())
        {
            xbim_set_error("xbim_shell_make_solid: resulting solid is null");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(solid);
        if (!*outHandle)
        {
            xbim_set_error("xbim_shell_make_solid: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_shell_make_solid");
        xbim_set_error("xbim_shell_make_solid: OCCT exception");
        return XBIM_ERROR;
    }
}

/* ── xbim_shell_build_closed_shell ─────────────────────────────────────── */

XBIM_EXPORT XbimResult XBIM_CALL xbim_shell_build_closed_shell(
    XbimContextHandle        ctx,
    const XbimShapeHandle*   faceHandles,
    int                      numFaces,
    double                   tolerance,
    XbimShapeHandle*         outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_shell_build_closed_shell: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!faceHandles)
    {
        xbim_set_error("xbim_shell_build_closed_shell: faceHandles is NULL");
        return XBIM_INVALID_ARG;
    }
    if (numFaces < 4)
    {
        xbim_set_error("xbim_shell_build_closed_shell: need >= 4 faces for a closed shell");
        return XBIM_INVALID_ARG;
    }

    try
    {
        /* Use BRepOffsetAPI_Sewing to merge coincident edges between faces */
        BRepOffsetAPI_Sewing sewing(tolerance);

        int addedCount = 0;
        for (int i = 0; i < numFaces; i++)
        {
            if (!faceHandles[i])
                continue;

            const TopoDS_Shape& shape = faceHandles[i]->shape;
            if (shape.IsNull())
                continue;

            if (shape.ShapeType() == TopAbs_FACE)
            {
                sewing.Add(shape);
                addedCount++;
            }
            else
            {
                /* Extract faces from other shape types */
                for (TopExp_Explorer exp(shape, TopAbs_FACE); exp.More(); exp.Next())
                {
                    sewing.Add(exp.Current());
                    addedCount++;
                }
            }
        }

        if (addedCount < 4)
        {
            xbim_set_error("xbim_shell_build_closed_shell: fewer than 4 valid faces");
            return XBIM_NULL_SHAPE;
        }

        sewing.Perform();

        TopoDS_Shape sewedShape = sewing.SewedShape();
        if (sewedShape.IsNull())
        {
            xbim_set_error("xbim_shell_build_closed_shell: sewing produced null shape");
            return XBIM_NULL_SHAPE;
        }

        /* Extract a shell from the sewed result */
        TopoDS_Shell shell;
        if (sewedShape.ShapeType() == TopAbs_SHELL)
        {
            shell = TopoDS::Shell(sewedShape);
        }
        else
        {
            TopExp_Explorer exp(sewedShape, TopAbs_SHELL);
            if (exp.More())
                shell = TopoDS::Shell(exp.Current());
        }

        if (shell.IsNull())
        {
            xbim_set_error("xbim_shell_build_closed_shell: sewing did not produce a shell");
            xbim_log_warning(ctx, "Sewing produced shape type %d instead of shell",
                (int)sewedShape.ShapeType());
            return XBIM_NULL_SHAPE;
        }

        /* Use ShapeFix_Solid to convert shell to solid with proper orientation */
        ShapeFix_Solid solidFixer;
        solidFixer.SetPrecision(tolerance);
        solidFixer.SetMinTolerance(tolerance);
        solidFixer.SetMaxTolerance(tolerance * 10);

        TopoDS_Solid solid = solidFixer.SolidFromShell(shell);
        if (solid.IsNull())
        {
            /* Fallback: try BRepBuilderAPI_MakeSolid */
            xbim_log_warning(ctx, "ShapeFix_Solid failed, trying BRepBuilderAPI_MakeSolid");
            BRepBuilderAPI_MakeSolid solidMaker(shell);
            if (!solidMaker.IsDone())
            {
                xbim_set_error("xbim_shell_build_closed_shell: cannot create solid from shell");
                return XBIM_NULL_SHAPE;
            }
            solid = solidMaker.Solid();
        }

        if (solid.IsNull())
        {
            xbim_set_error("xbim_shell_build_closed_shell: resulting solid is null");
            return XBIM_NULL_SHAPE;
        }

        /* Run ShapeFix_Shape to fix topology issues from sewing */
        Handle(ShapeFix_Shape) shapeFixer = new ShapeFix_Shape(solid);
        shapeFixer->SetPrecision(tolerance);
        shapeFixer->SetMinTolerance(tolerance);
        shapeFixer->SetMaxTolerance(tolerance * 10);
        shapeFixer->Perform();
        TopoDS_Shape fixedShape = shapeFixer->Shape();
        if (!fixedShape.IsNull() && fixedShape.ShapeType() == TopAbs_SOLID)
            solid = TopoDS::Solid(fixedShape);

        *outHandle = xbim_shape_create_from(solid);
        if (!*outHandle)
        {
            xbim_set_error("xbim_shell_build_closed_shell: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_shell_build_closed_shell");
        xbim_set_error("xbim_shell_build_closed_shell: OCCT exception");
        return XBIM_ERROR;
    }
}
