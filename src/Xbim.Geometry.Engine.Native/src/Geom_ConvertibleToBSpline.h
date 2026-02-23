/*
 * Geom_ConvertibleToBSpline.h
 *
 * Abstract base class for custom Geom_Curve subclasses that can be
 * approximated as B-spline curves. Provides ToBSpline() conversion
 * over the full parameter range or a sub-range.
 *
 * Used as the parent of Geom_GradientCurve and Geom_SegmentedReferenceCurve.
 */

#pragma once

#include <Geom_Curve.hxx>
#include <Geom_BSplineCurve.hxx>
#include <Standard_Type.hxx>
#include <Standard_DefineHandle.hxx>

class Geom_ConvertibleToBSpline;
DEFINE_STANDARD_HANDLE(Geom_ConvertibleToBSpline, Geom_Curve)

class Geom_ConvertibleToBSpline : public Geom_Curve
{
public:
    virtual ~Geom_ConvertibleToBSpline() = default;

    /*
     * Approximate the full curve as a B-spline by sampling nbPoints points.
     */
    virtual Handle(Geom_BSplineCurve) ToBSpline(int nbPoints) const = 0;

    /*
     * Approximate a sub-range [startParam, endParam] as a B-spline
     * by sampling nbPoints points.
     */
    virtual Handle(Geom_BSplineCurve) ToBSpline(
        double startParam, double endParam, int nbPoints) const = 0;

protected:
    Geom_ConvertibleToBSpline() = default;
};
