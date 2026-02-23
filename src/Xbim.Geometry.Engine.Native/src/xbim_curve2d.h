/*
 * xbim_curve2d.h
 *
 * Internal definition of the XbimCurve2d struct.
 * Wraps a heap-allocated Geom2d_Curve with lifecycle management.
 *
 * Not part of the public C API - used by native implementation code only.
 */

#ifndef XBIM_CURVE2D_H
#define XBIM_CURVE2D_H

#include "xbim_geometry_api.h"

#include <Geom2d_Curve.hxx>

struct XbimCurve2d_
{
    Handle(Geom2d_Curve) curve;
};

/*
 * Allocate a new XbimCurve2d_ on the heap from a Geom2d_Curve handle.
 * Returns nullptr if the curve handle is null or allocation fails.
 */
XbimCurve2dHandle xbim_curve2d_create_from(const Handle(Geom2d_Curve)& curve);

#endif /* XBIM_CURVE2D_H */
