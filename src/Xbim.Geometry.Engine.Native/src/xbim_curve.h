/*
 * xbim_curve.h
 *
 * Internal definition of the XbimCurve struct.
 * Wraps a heap-allocated Geom_Curve with lifecycle management.
 *
 * Not part of the public C API - used by native implementation code only.
 */

#ifndef XBIM_CURVE_H
#define XBIM_CURVE_H

#include "xbim_geometry_api.h"

#include <Geom_Curve.hxx>

struct XbimCurve_
{
    Handle(Geom_Curve) curve;
};

/*
 * Allocate a new XbimCurve_ on the heap from a Geom_Curve handle.
 * Returns nullptr if the curve handle is null or allocation fails.
 */
XbimCurveHandle xbim_curve_create_from(const Handle(Geom_Curve)& curve);

#endif /* XBIM_CURVE_H */
