/*
 * xbim_shell.cpp
 *
 * Implements shell construction and repair operations
 *
 */

#include "xbim_shell.h"
#include "xbim_shape.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"


#include <TopoDS.hxx>
#include <TopoDS_Shell.hxx>
#include <TopoDS_Solid.hxx>
#include <TopoDS_Compound.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Wire.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Vertex.hxx>
#include <TopoDS_Shape.hxx>
#include <BRep_Builder.hxx>
#include <BRep_Tool.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeSolid.hxx>
#include <BRepBuilderAPI_VertexInspector.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepCheck_Shell.hxx>
#include <BRepClass3d_SolidClassifier.hxx>
#include <BRepGProp.hxx>
#include <BRepOffsetAPI_Sewing.hxx>
#include <BRepTools.hxx>
#include <Geom_Plane.hxx>
#include <GeomPlate_BuildAveragePlane.hxx>
#include <GProp_GProps.hxx>
#include <NCollection_CellFilter.hxx>
#include <Precision.hxx>
#include <ShapeFix_Shape.hxx>
#include <ShapeFix_Shell.hxx>
#include <ShapeFix_Solid.hxx>
#include <ShapeFix_Wire.hxx>
#include <TColgp_HArray1OfPnt.hxx>
#include <TopExp.hxx>
#include <TopExp_Explorer.hxx>
#include <TopAbs_ShapeEnum.hxx>
#include <TopTools_IndexedMapOfShape.hxx>
#include <TopTools_SequenceOfShape.hxx>
#include <Standard_Failure.hxx>
#include <unordered_map>
#include <vector>

#pragma region Shell Construction

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

#pragma endregion

#pragma region Shell Conversion

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

        ShapeFix_Solid sfs;
        double tol = Precision::Confusion();
        sfs.SetPrecision(tol);
        sfs.SetMinTolerance(tol);
        sfs.SetMaxTolerance(tol * 10);

        /* Check if shell topology is valid */
        BRepCheck_Analyzer topologyChecker(shell, false, false, false);
        if (topologyChecker.IsValid())
        {
            /* Valid shell — simple upgrade */
            TopoDS_Solid solid = sfs.SolidFromShell(shell);
            if (!solid.IsNull())
            {
                BRepClass3d_SolidClassifier class3d(solid);
                class3d.PerformInfinitePoint(Precision::Confusion());
                if (class3d.State() == TopAbs_IN)
                    solid.Reverse();
                *outHandle = xbim_shape_create_from(solid);
            }
            else
            {
                /* Fallback to BRepBuilderAPI_MakeSolid */
                BRepBuilderAPI_MakeSolid solidMaker(shell);
                if (!solidMaker.IsDone())
                {
                    xbim_set_error("xbim_shell_make_solid: could not create solid from shell");
                    return XBIM_NULL_SHAPE;
                }
                TopoDS_Solid fallback = solidMaker.Solid();
                BRepClass3d_SolidClassifier class3d(fallback);
                class3d.PerformInfinitePoint(Precision::Confusion());
                if (class3d.State() == TopAbs_IN)
                    fallback.Reverse();
                *outHandle = xbim_shape_create_from(fallback);
            }
        }
        else
        {
            /* Invalid topology — may contain disjoint regions.
               Use ShapeFix_Shape to split and upgrade each piece. */
            ShapeFix_Shape shapeFixer(shell);
            if (shapeFixer.Perform())
            {
                TopoDS_Shape fixResult = shapeFixer.Shape();
                if (!fixResult.IsNull() && fixResult.ShapeType() == TopAbs_SHELL)
                {
                    /* Single shell result — upgrade directly */
                    TopoDS_Solid solid = sfs.SolidFromShell(TopoDS::Shell(fixResult));
                    if (!solid.IsNull())
                    {
                        BRepClass3d_SolidClassifier class3d(solid);
                        class3d.PerformInfinitePoint(Precision::Confusion());
                        if (class3d.State() == TopAbs_IN)
                            solid.Reverse();
                        *outHandle = xbim_shape_create_from(solid);
                    }
                    else
                    {
                        xbim_set_error("xbim_shell_make_solid: SolidFromShell failed on fixed shell");
                        return XBIM_NULL_SHAPE;
                    }
                }
                else
                {
                    /* Compound or other — iterate sub-shells and make solids */
                    BRep_Builder b;
                    TopoDS_Compound solidCompound;
                    b.MakeCompound(solidCompound);
                    int solidCount = 0;

                    for (TopoDS_Iterator it(fixResult); it.More(); it.Next())
                    {
                        if (it.Value().ShapeType() == TopAbs_SHELL)
                        {
                            const TopoDS_Shell& subShell = TopoDS::Shell(it.Value());
                            if (subShell.NbChildren() >= 4)
                            {
                                TopoDS_Solid subSolid = sfs.SolidFromShell(subShell);
                                if (!subSolid.IsNull())
                                {
                                    BRepClass3d_SolidClassifier class3d(subSolid);
                                    class3d.PerformInfinitePoint(Precision::Confusion());
                                    if (class3d.State() == TopAbs_IN)
                                        subSolid.Reverse();
                                    b.Add(solidCompound, subSolid);
                                    solidCount++;
                                }
                            }
                        }
                        else if (it.Value().ShapeType() == TopAbs_SOLID)
                        {
                            b.Add(solidCompound, it.Value());
                            solidCount++;
                        }
                    }

                    if (solidCount == 1)
                    {
                        TopoDS_Iterator single(solidCompound);
                        *outHandle = xbim_shape_create_from(single.Value());
                    }
                    else if (solidCount > 1)
                    {
                        *outHandle = xbim_shape_create_from(solidCompound);
                    }
                    else
                    {
                        /* No solids from splitting — try simple upgrade as fallback */
                        TopoDS_Solid solid = sfs.SolidFromShell(shell);
                        if (!solid.IsNull())
                        {
                            BRepClass3d_SolidClassifier class3d(solid);
                            class3d.PerformInfinitePoint(Precision::Confusion());
                            if (class3d.State() == TopAbs_IN)
                                solid.Reverse();
                            *outHandle = xbim_shape_create_from(solid);
                        }
                        else
                        {
                            xbim_set_error("xbim_shell_make_solid: cannot create solid from shell");
                            return XBIM_NULL_SHAPE;
                        }
                    }
                }
            }
            else
            {
                /* ShapeFix_Shape didn't fix — try simple upgrade */
                TopoDS_Solid solid = sfs.SolidFromShell(shell);
                if (!solid.IsNull())
                {
                    BRepClass3d_SolidClassifier class3d(solid);
                    class3d.PerformInfinitePoint(Precision::Confusion());
                    if (class3d.State() == TopAbs_IN)
                        solid.Reverse();
                    *outHandle = xbim_shape_create_from(solid);
                }
                else
                {
                    BRepBuilderAPI_MakeSolid solidMaker(shell);
                    if (!solidMaker.IsDone())
                    {
                        xbim_set_error("xbim_shell_make_solid: could not create solid from shell");
                        return XBIM_NULL_SHAPE;
                    }
                    TopoDS_Solid fallback = solidMaker.Solid();
                    BRepClass3d_SolidClassifier class3d(fallback);
                    class3d.PerformInfinitePoint(Precision::Confusion());
                    if (class3d.State() == TopAbs_IN)
                        fallback.Reverse();
                    *outHandle = xbim_shape_create_from(fallback);
                }
            }
        }

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


/*
 * EdgeId — hash key for shared-edge deduplication.
 * Stores start/end vertex indices; treats (a,b) and (b,a) as the same edge.
 */
struct EdgeId
{
    const int Start;
    const int End;
    const bool Reversed;

    EdgeId(int a, int b) : Start(a), End(b), Reversed(a > b) {}

    int LowIndex() const { return Reversed ? End : Start; }
    int HighIndex() const { return Reversed ? Start : End; }
    bool IsValid() const { return Start != End; }
    int Hash() const { return LowIndex() ^ (HighIndex() << 1); }
    bool Equals(const EdgeId& rhs) const
    {
        return (Start == rhs.Start && End == rhs.End) ||
               (Start == rhs.End && End == rhs.Start);
    }
};

struct EdgeIdHash {
    std::size_t operator()(const EdgeId& k) const { return (std::size_t)k.Hash(); }
};

struct EdgeIdEqual {
    bool operator()(const EdgeId& lhs, const EdgeId& rhs) const {
        return lhs.Equals(rhs);
    }
};

/*
 * Check whether all points in the array are collinear within tolerance.
 */
static Standard_Boolean ArePointsCollinear(
    const Handle(TColgp_HArray1OfPnt)& thePoints, Standard_Real theLinTol)
{
    if (thePoints.IsNull())
        return Standard_True;

    const Standard_Integer low = thePoints->Lower();
    const Standard_Integer high = thePoints->Upper();
    const Standard_Integer count = high - low + 1;
    if (count < 3)
        return Standard_True;

    gp_Pnt P0 = thePoints->Value(low);
    gp_Pnt P1;
    Standard_Boolean found = Standard_False;
    for (Standard_Integer i = low + 1; i <= high; ++i)
    {
        P1 = thePoints->Value(i);
        if (P0.SquareDistance(P1) > theLinTol * theLinTol)
        {
            found = Standard_True;
            break;
        }
    }
    if (!found)
        return Standard_True;

    const gp_Vec dirVec(P0, P1);
    const gp_Lin refLine(P0, gp_Dir(dirVec));
    for (Standard_Integer i = low; i <= high; ++i)
    {
        const gp_Pnt& P = thePoints->Value(i);
        if (refLine.Distance(P) > theLinTol)
            return Standard_False;
    }
    return Standard_True;
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_shell_build_connected_face_set(
    XbimContextHandle  ctx,
    const double*      allPointsXYZ,
    int                numPoints,
    const int*         faceData,
    int                faceDataLength,
    int                numFaces,
    int                flags,
    XbimShapeHandle*   outHandle)
{
    bool makeSolid      = (flags & XBIM_FACESET_MAKE_SOLID)   != 0;
    bool upgradeFaceSets = (flags & XBIM_FACESET_UPGRADE)     != 0;
    bool skipWinding    = (flags & XBIM_FACESET_SKIP_WINDING) != 0;
    bool skipWireFix    = (flags & XBIM_FACESET_SKIP_WIREFIX) != 0;
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_shell_build_connected_face_set: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (!allPointsXYZ || numPoints < 3)
    {
        xbim_set_error("xbim_shell_build_connected_face_set: invalid points data");
        return XBIM_INVALID_ARG;
    }
    if (!faceData || faceDataLength < 1 || numFaces < 1)
    {
        xbim_set_error("xbim_shell_build_connected_face_set: invalid face data");
        return XBIM_INVALID_ARG;
    }

    try
    {
        double tol = ctx->minimumGap > 0 ? ctx->minimumGap : Precision::Confusion();

        /* ---- Phase 1: Vertex deduplication ---- */
        BRepBuilderAPI_VertexInspector inspector(tol);
        NCollection_CellFilter<BRepBuilderAPI_VertexInspector> vertexCellFilter(2, tol);
        TopTools_SequenceOfShape vertices;
        BRep_Builder builder;

        /* Map input point index → deduplicated vertex index (1-based, matching OCCT sequences) */
        std::vector<int> pointToVertex(numPoints);

        for (int i = 0; i < numPoints; i++)
        {
            gp_XYZ coord(allPointsXYZ[i * 3], allPointsXYZ[i * 3 + 1], allPointsXYZ[i * 3 + 2]);
            inspector.ClearResList();
            inspector.SetCurrent(coord);
            vertexCellFilter.Inspect(coord, inspector);
            const TColStd_ListOfInteger& results = inspector.ResInd();

            if (results.Size() > 0)
            {
                pointToVertex[i] = results.First();
            }
            else
            {
                TopoDS_Vertex vertex;
                builder.MakeVertex(vertex, coord, tol);
                inspector.Add(coord);
                vertices.Append(vertex);
                vertexCellFilter.Add(vertices.Size(), coord);
                pointToVertex[i] = vertices.Size();
            }
        }

        /* ---- Phase 1b: Parse face data and compute planes ---- */

        /* Each face stores: [numBounds, bound0..., bound1..., ...]
         * Each bound stores: [numPointIndices, isOuter, idx0, idx1, ..., idxN] */
        struct BoundData {
            bool isOuter;
            std::vector<int> vertexIndices; /* 1-based vertex indices (after dedup) */
        };
        struct FaceRecord {
            std::vector<BoundData> bounds;
            Handle(Geom_Plane) plane;
        };

        std::vector<FaceRecord> faceRecords;
        faceRecords.reserve(numFaces);

        int pos = 0;
        for (int f = 0; f < numFaces && pos < faceDataLength; f++)
        {
            int nbBounds = faceData[pos++];
            FaceRecord rec;

            /* Collect all points on this face for plane computation */
            int totalPointsOnFace = 0;
            int savedPos = pos;

            /* First pass: count total points */
            int tmpPos = pos;
            for (int b = 0; b < nbBounds && tmpPos < faceDataLength; b++)
            {
                int nbPts = faceData[tmpPos++];
                tmpPos++; /* skip isOuter */
                totalPointsOnFace += nbPts;
                tmpPos += nbPts;
            }

            Handle(TColgp_HArray1OfPnt) pointsOnFace =
                new TColgp_HArray1OfPnt(1, totalPointsOnFace > 0 ? totalPointsOnFace : 1);
            int ptIdx = 1;

            /* Second pass: parse bounds and collect points */
            for (int b = 0; b < nbBounds && pos < faceDataLength; b++)
            {
                int nbPts = faceData[pos++];
                int isOuter = faceData[pos++];

                BoundData bd;
                bd.isOuter = (isOuter != 0);

                for (int p = 0; p < nbPts && pos < faceDataLength; p++)
                {
                    int ptIndex = faceData[pos++];
                    if (ptIndex < 0 || ptIndex >= numPoints)
                        continue;

                    int vertIdx = pointToVertex[ptIndex];
                    bd.vertexIndices.push_back(vertIdx);

                    if (ptIdx <= totalPointsOnFace)
                    {
                        gp_Pnt pt(allPointsXYZ[ptIndex * 3],
                                  allPointsXYZ[ptIndex * 3 + 1],
                                  allPointsXYZ[ptIndex * 3 + 2]);
                        pointsOnFace->SetValue(ptIdx++, pt);
                    }
                }

                rec.bounds.push_back(std::move(bd));
            }

            /* Compute plane for this face */
            int uniquePoints = ptIdx - 1; /* actual number of points collected */
            if (uniquePoints >= 3 && uniquePoints <= 4 && nbBounds == 1)
            {
                /* Fast path for triangles: direct cross product.
                   A triangular bound has 3 unique vertices + closing repeat = 4 points.
                   This avoids the expensive GeomPlate_BuildAveragePlane (SVD). */
                gp_Pnt p1 = pointsOnFace->Value(1);
                gp_Pnt p2 = pointsOnFace->Value(2);
                gp_Pnt p3 = pointsOnFace->Value(3);
                gp_Vec v1(p1, p2), v2(p1, p3);
                gp_Vec normal = v1.Crossed(v2);
                if (normal.SquareMagnitude() < tol * tol)
                {
                    xbim_log_warning(ctx, "Face %d: degenerate triangle, skipping", f);
                    continue;
                }
                rec.plane = new Geom_Plane(p1, gp_Dir(normal));
            }
            else
            {
                /* General polygon: best-fit plane via eigenvalue decomposition */
                if (ArePointsCollinear(pointsOnFace, tol))
                {
                    xbim_log_warning(ctx, "Face %d: all points are collinear, skipping", f);
                    continue;
                }

                GeomPlate_BuildAveragePlane averagePlaneBuilder(
                    pointsOnFace, pointsOnFace->Upper(), tol, 1, 2);

                if (!averagePlaneBuilder.IsPlane())
                {
                    xbim_log_warning(ctx, "Face %d: could not compute a planar surface, skipping", f);
                    continue;
                }

                rec.plane = averagePlaneBuilder.Plane();
            }
            faceRecords.push_back(std::move(rec));
        }

        if (faceRecords.empty())
        {
            xbim_set_error("xbim_shell_build_connected_face_set: no valid faces after plane computation");
            return XBIM_NULL_SHAPE;
        }

        /* ---- Phase 2: Build edges and faces with shared topology ---- */
        std::unordered_map<EdgeId, int, EdgeIdHash, EdgeIdEqual> uniqueEdges;
        TopTools_SequenceOfShape edges;
        TopoDS_Shell shell;
        builder.MakeShell(shell);
        int faceCount = 0;

        for (auto& rec : faceRecords)
        {
            if (rec.plane.IsNull())
                continue;

            TopoDS_Face theFace;
            builder.MakeFace(theFace, rec.plane, tol);

            for (auto& bd : rec.bounds)
            {
                if (bd.vertexIndices.size() < 3)
                    continue;

                TopoDS_Wire topoWire;
                builder.MakeWire(topoWire);

                auto it = bd.vertexIndices.cbegin();
                int a = *it;
                TopoDS_Vertex aVert = TopoDS::Vertex(vertices.Value(a));
                ++it;

                for (; it != bd.vertexIndices.cend(); ++it)
                {
                    int b = *it;
                    EdgeId edgeId(a, b);
                    TopoDS_Vertex bVert = TopoDS::Vertex(vertices.Value(b));

                    if (edgeId.IsValid())
                    {
                        auto inserted = uniqueEdges.try_emplace(edgeId, edges.Size() + 1);
                        if (inserted.second)
                        {
                            /* New edge */
                            BRepBuilderAPI_MakeEdge edgeMaker(aVert, bVert);
                            TopoDS_Edge edge = edgeMaker.Edge();
                            edges.Append(edge);
                            builder.Add(topoWire, edge);
                        }
                        else
                        {
                            /* Existing edge — reuse, possibly reversed */
                            TopoDS_Edge edge = TopoDS::Edge(
                                edges.Value(inserted.first->second));
                            const EdgeId& foundEdgeId = inserted.first->first;
                            if (foundEdgeId.Start == edgeId.Start)
                                builder.Add(topoWire, edge);
                            else
                                builder.Add(topoWire, TopoDS::Edge(edge.Reversed()));
                        }
                    }

                    aVert = bVert;
                    a = b;
                }

                if (topoWire.NbChildren() > 2)
                    builder.Add(theFace, topoWire);
            }
            if (theFace.NbChildren() == 0)
                continue;

            if (!skipWireFix || !skipWinding)
            {
                /* ---- Validate outer wire winding ---- */
                TopoDS_Wire outerBound = BRepTools::OuterWire(theFace);

                if (!skipWireFix)
                {
                    ShapeFix_Wire wireFixer(outerBound, theFace, tol);
                    wireFixer.ClearModes();
                    wireFixer.FixVertexToleranceMode() = true;
                    wireFixer.Perform();
                }

                if (!skipWinding)
                {
                    for (TopExp_Explorer exp(theFace, TopAbs_WIRE); exp.More(); exp.Next())
                    {
                        const TopoDS_Wire& wire = TopoDS::Wire(exp.Current());
                        if (wire.IsEqual(outerBound))
                        {
                            TopoDS_Face tmpFace;
                            builder.MakeFace(tmpFace, rec.plane, tol);
                            builder.Add(tmpFace, wire);
                            GProp_GProps gProps;
                            BRepGProp::SurfaceProperties(tmpFace, gProps, tol);
                            double area = gProps.Mass();
                            if (std::abs(area) < Precision::Confusion())
                            {
                                theFace.EmptyCopy();
                                break;
                            }
                            bool isCounterClockwise = area > 0;
                            if (!isCounterClockwise)
                                rec.plane->SetAxis(rec.plane->Axis().Reversed());
                            break;
                        }
                    }

                    if (theFace.NbChildren() == 0)
                        continue;
                }

                /* ---- Validate inner wire winding ---- */
                if (!skipWinding && theFace.NbChildren() > 1)
                {
                    bool faceNeedsToBeRebuilt = false;
                    TopoDS_ListOfShape innerWires;

                    for (TopExp_Explorer exp(theFace, TopAbs_WIRE); exp.More(); exp.Next())
                    {
                        const TopoDS_Shape& wireShape = exp.Current();
                        if (!wireShape.IsEqual(outerBound))
                        {
                            if (!skipWireFix)
                            {
                                ShapeFix_Wire innerWireFixer(TopoDS::Wire(wireShape), theFace, tol);
                                innerWireFixer.ClearModes();
                                innerWireFixer.FixVertexToleranceMode() = true;
                                innerWireFixer.Perform();
                            }

                            TopoDS_Face tmpFace;
                            builder.MakeFace(tmpFace, rec.plane, tol);
                            builder.Add(tmpFace, wireShape);
                            GProp_GProps gProps;
                            BRepGProp::SurfaceProperties(tmpFace, gProps, tol);
                            double area = gProps.Mass();
                            if (std::abs(area) < Precision::Confusion())
                                continue;
                            bool isCounterClockwise = area > 0;
                            if (isCounterClockwise)
                            {
                                /* Inner wire should be CW — reverse it */
                                innerWires.Append(wireShape.Reversed());
                                faceNeedsToBeRebuilt = true;
                            }
                            else
                            {
                                innerWires.Append(wireShape);
                            }
                        }
                    }

                    if (faceNeedsToBeRebuilt)
                    {
                        theFace.EmptyCopy();
                        builder.Add(theFace, outerBound);
                        for (auto it = innerWires.cbegin(); it != innerWires.cend(); ++it)
                            builder.Add(theFace, *it);
                    }
                }
            }

            faceCount++;
            builder.Add(shell, theFace);
        }

        if (faceCount == 0)
        {
            xbim_set_error("xbim_shell_build_connected_face_set: no valid faces were built");
            return XBIM_NULL_SHAPE;
        }

        /* ---- Phase 3: Shell fix + optional solid upgrade ---- */
        BRepCheck_Shell checker(shell);
        BRepCheck_Status shellStatus = checker.Orientation();

        if (shellStatus != BRepCheck_NoError)
        {
            ShapeFix_Shell shapeFixer(shell);
            if (shapeFixer.Perform())
            {
                TopoDS_Shape fixedShape = shapeFixer.Shape();
                if (!fixedShape.IsNull() && fixedShape.ShapeType() == TopAbs_SHELL)
                {
                    shell = shapeFixer.Shell();
                    checker.Init(shell);
                }
            }

            if (checker.Closed() == BRepCheck_NoError)
            {
                shell.Closed(Standard_True);
                shell.Checked(Standard_True);
            }
            else
            {
                ShapeFix_Shape shapeFixAll(shell);
                if (shapeFixAll.Perform())
                {
                    TopoDS_Shape result = shapeFixAll.Shape();
                    if (!result.IsNull() && result.ShapeType() == TopAbs_SHELL)
                        shell = TopoDS::Shell(result);
                }
            }
        }
        else
        {
            shell.Closed(Standard_True);
            shell.Checked(Standard_True);
        }

        if (makeSolid)
        {
            ShapeFix_Solid sfs;
            sfs.SetPrecision(tol);
            sfs.SetMinTolerance(tol);
            sfs.SetMaxTolerance(tol * 10);

            if (upgradeFaceSets)
            {
                /* Upgrade path: detect multi-solid shells and split them.
                   A single IIfcClosedShell may actually contain multiple
                   disjoint solids — a common IFC authoring error. */
                BRepCheck_Analyzer topologyChecker(shell, false, false, false);
                if (topologyChecker.IsValid())
                {
                    /* Valid shell — simple upgrade */
                    TopoDS_Solid solid = sfs.SolidFromShell(shell);
                    if (!solid.IsNull())
                    {
                        BRepClass3d_SolidClassifier class3d(solid);
                        class3d.PerformInfinitePoint(Precision::Confusion());
                        if (class3d.State() == TopAbs_IN)
                            solid.Reverse();
                        *outHandle = xbim_shape_create_from(solid);
                    }
                    else
                    {
                        xbim_set_error("xbim_shell_build_connected_face_set: SolidFromShell failed");
                        return XBIM_NULL_SHAPE;
                    }
                }
                else
                {
                    /* Invalid topology — likely multiple disjoint solids.
                       Use ShapeFix_Shape to split and upgrade each piece. */
                    ShapeFix_Shape shapeFixer(shell);
                    if (shapeFixer.Perform())
                    {
                        BRep_Builder b;
                        TopoDS_Compound solidCompound;
                        b.MakeCompound(solidCompound);
                        int solidCount = 0;

                        for (TopoDS_Iterator it(shapeFixer.Shape()); it.More(); it.Next())
                        {
                            if (it.Value().ShapeType() == TopAbs_SHELL)
                            {
                                const TopoDS_Shell& subShell = TopoDS::Shell(it.Value());
                                if (subShell.NbChildren() >= 4)
                                {
                                    TopoDS_Solid subSolid = sfs.SolidFromShell(subShell);
                                    if (!subSolid.IsNull())
                                    {
                                        BRepClass3d_SolidClassifier class3d(subSolid);
                                        class3d.PerformInfinitePoint(Precision::Confusion());
                                        if (class3d.State() == TopAbs_IN)
                                            subSolid.Reverse();
                                        b.Add(solidCompound, subSolid);
                                        solidCount++;
                                    }
                                }
                                else
                                {
                                    xbim_log_warning(ctx,
                                        "Sub-shell has fewer than 4 faces, skipping");
                                }
                            }
                            else if (it.Value().ShapeType() == TopAbs_SOLID)
                            {
                                b.Add(solidCompound, it.Value());
                                solidCount++;
                            }
                        }

                        if (solidCount == 1)
                        {
                            /* Single solid — unwrap from compound */
                            TopoDS_Iterator single(solidCompound);
                            *outHandle = xbim_shape_create_from(single.Value());
                        }
                        else if (solidCount > 1)
                        {
                            *outHandle = xbim_shape_create_from(solidCompound);
                        }
                        else
                        {
                            /* Fix didn't produce any solids — fall through to simple upgrade */
                            TopoDS_Solid solid = sfs.SolidFromShell(shell);
                            if (!solid.IsNull())
                            {
                                BRepClass3d_SolidClassifier class3d(solid);
                                class3d.PerformInfinitePoint(Precision::Confusion());
                                if (class3d.State() == TopAbs_IN)
                                    solid.Reverse();
                                *outHandle = xbim_shape_create_from(solid);
                            }
                            else
                            {
                                xbim_set_error("xbim_shell_build_connected_face_set: cannot create solid from shell");
                                return XBIM_NULL_SHAPE;
                            }
                        }
                    }
                    else
                    {
                        /* ShapeFix_Shape didn't fix anything — simple upgrade as fallback */
                        TopoDS_Solid solid = sfs.SolidFromShell(shell);
                        if (!solid.IsNull())
                        {
                            BRepClass3d_SolidClassifier class3d(solid);
                            class3d.PerformInfinitePoint(Precision::Confusion());
                            if (class3d.State() == TopAbs_IN)
                                solid.Reverse();
                            *outHandle = xbim_shape_create_from(solid);
                        }
                        else
                        {
                            xbim_set_error("xbim_shell_build_connected_face_set: cannot create solid from shell");
                            return XBIM_NULL_SHAPE;
                        }
                    }
                }
            }
            else
            {
                /* Fast path: no upgrade analysis, just convert shell to solid */
                TopoDS_Solid solid = sfs.SolidFromShell(shell);
                if (solid.IsNull())
                {
                    xbim_log_warning(ctx, "ShapeFix_Solid failed, trying BRepBuilderAPI_MakeSolid");
                    BRepBuilderAPI_MakeSolid solidMaker(shell);
                    if (!solidMaker.IsDone())
                    {
                        xbim_set_error("xbim_shell_build_connected_face_set: cannot create solid from shell");
                        return XBIM_NULL_SHAPE;
                    }
                    solid = solidMaker.Solid();
                }

                BRepClass3d_SolidClassifier class3d(solid);
                class3d.PerformInfinitePoint(Precision::Confusion());
                if (class3d.State() == TopAbs_IN)
                    solid.Reverse();

                *outHandle = xbim_shape_create_from(solid);
            }
        }
        else
        {
            *outHandle = xbim_shape_create_from(shell);
        }

        if (!*outHandle)
        {
            xbim_set_error("xbim_shell_build_connected_face_set: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_shell_build_connected_face_set");
        xbim_set_error("xbim_shell_build_connected_face_set: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion
