/*
 * xbim_shape.h
 *
 * Internal definition of the XbimShape struct.
 * Wraps a heap-allocated TopoDS_Shape with lifecycle management.
 *
 * Not part of the public C API - used by native implementation code only.
 */

#ifndef XBIM_SHAPE_H
#define XBIM_SHAPE_H

#include "xbim_geometry_api.h"

#include <TopoDS_Shape.hxx>

struct XbimShape_
{
    TopoDS_Shape shape;
};

/*
 * Allocate a new XbimShape_ on the heap from a TopoDS_Shape.
 * Returns nullptr on allocation failure.
 */
XbimShapeHandle xbim_shape_create_from(const TopoDS_Shape& shape);

#endif /* XBIM_SHAPE_H */
