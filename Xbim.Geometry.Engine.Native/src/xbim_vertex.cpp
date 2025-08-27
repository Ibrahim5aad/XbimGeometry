/*
 * xbim_vertex.cpp
 *
 * Implements vertex construction and query functions via the flat C API.
 * Ports NVertexFactory methods from the C++/CLI engine:
 *   - Build a vertex at a 3D point with a given tolerance
 *   - Query the 3D coordinates of a vertex
 */

#include "xbim_vertex.h"
#include "xbim_shape.h"
#include "xbim_context.h"
#include "xbim_error.h"
#include "xbim_logging.h"

#include <gp_Pnt.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Vertex.hxx>
#include <BRep_Builder.hxx>
#include <BRep_Tool.hxx>
#include <Standard_Failure.hxx>

#pragma region Vertex Operations

XBIM_EXPORT XbimResult XBIM_CALL xbim_vertex_build(
    XbimContextHandle ctx,
    double x, double y, double z,
    double tolerance,
    XbimShapeHandle* outHandle)
{
    xbim_clear_error();

    if (!outHandle)
    {
        xbim_set_error("xbim_vertex_build: outHandle is NULL");
        return XBIM_INVALID_ARG;
    }
    *outHandle = nullptr;

    if (tolerance <= 0.0)
    {
        xbim_set_error("xbim_vertex_build: tolerance must be positive");
        xbim_log_warning(ctx, "Cannot build vertex: non-positive tolerance %g", tolerance);
        return XBIM_INVALID_ARG;
    }

    try
    {
        gp_Pnt pnt(x, y, z);

        BRep_Builder builder;
        TopoDS_Vertex vertex;
        builder.MakeVertex(vertex, pnt, tolerance);

        if (vertex.IsNull())
        {
            xbim_set_error("xbim_vertex_build: resulting vertex is null");
            return XBIM_NULL_SHAPE;
        }

        *outHandle = xbim_shape_create_from(vertex);
        if (!*outHandle)
        {
            xbim_set_error("xbim_vertex_build: memory allocation failed");
            return XBIM_ERROR;
        }

        return XBIM_OK;
    }
    catch (const Standard_Failure& e)
    {
        xbim_log_occt_failure(ctx, e, "xbim_vertex_build");
        xbim_set_error("xbim_vertex_build: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_vertex_point(
    XbimShapeHandle vertexHandle,
    double* outX, double* outY, double* outZ)
{
    xbim_clear_error();

    if (!outX || !outY || !outZ)
    {
        xbim_set_error("xbim_vertex_point: output pointer(s) are NULL");
        return XBIM_INVALID_ARG;
    }
    *outX = 0.0;
    *outY = 0.0;
    *outZ = 0.0;

    if (!vertexHandle)
    {
        xbim_set_error("xbim_vertex_point: vertexHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = vertexHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_VERTEX)
        {
            xbim_set_error("xbim_vertex_point: handle is not a vertex");
            return XBIM_INVALID_ARG;
        }

        const TopoDS_Vertex& vertex = TopoDS::Vertex(shape);
        gp_Pnt pnt = BRep_Tool::Pnt(vertex);

        *outX = pnt.X();
        *outY = pnt.Y();
        *outZ = pnt.Z();

        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_vertex_point: OCCT exception");
        return XBIM_ERROR;
    }
}


XBIM_EXPORT XbimResult XBIM_CALL xbim_vertex_tolerance(
    XbimShapeHandle vertexHandle,
    double*         outTolerance)
{
    xbim_clear_error();

    if (!outTolerance)
    {
        xbim_set_error("xbim_vertex_tolerance: outTolerance is NULL");
        return XBIM_INVALID_ARG;
    }
    *outTolerance = 0.0;

    if (!vertexHandle)
    {
        xbim_set_error("xbim_vertex_tolerance: vertexHandle is NULL");
        return XBIM_INVALID_HANDLE;
    }

    try
    {
        const TopoDS_Shape& shape = vertexHandle->shape;
        if (shape.IsNull() || shape.ShapeType() != TopAbs_VERTEX)
        {
            xbim_set_error("xbim_vertex_tolerance: handle is not a vertex");
            return XBIM_INVALID_ARG;
        }

        const TopoDS_Vertex& vertex = TopoDS::Vertex(shape);
        *outTolerance = BRep_Tool::Tolerance(vertex);
        return XBIM_OK;
    }
    catch (const Standard_Failure&)
    {
        xbim_set_error("xbim_vertex_tolerance: OCCT exception");
        return XBIM_ERROR;
    }
}

#pragma endregion
