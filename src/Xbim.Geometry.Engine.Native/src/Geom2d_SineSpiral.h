/*
 * Geom2d_SineSpiral.h
 *
 * Sine spiral curve for IFC4x3 alignment geometry. Curvature is defined
 * by a sinusoidal function of arc length:
 *   kappa(s) = 1/C0 + sign(L1)*s/L1^2 + (1/S)*sin(2*pi*s/L)
 * where L = arc length span, C0 = constant term, L1 = linear term,
 * S = sine term.
 *
 * Heading is the analytical integral of curvature. Position is computed
 * via the base class Simpson's rule integration of the heading angle.
 */

#pragma once

#include "Geom2d_Spiral.h"

#include <Standard_DefineHandle.hxx>

class Geom2d_SineSpiral;
DEFINE_STANDARD_HANDLE(Geom2d_SineSpiral, Geom2d_Spiral)

class Geom2d_SineSpiral : public Geom2d_Spiral
{
public:
    Geom2d_SineSpiral(const gp_Ax22d& placement,
                      double sineTerm, double linearTerm, double constantTerm,
                      double startParam, double endParam);

    Standard_Real GetHeadingAt(Standard_Real s) const override;
    Standard_Real GetCurvatureAt(Standard_Real s) const override;

    Handle(Geom2d_Geometry) Copy() const override;

private:
    double _sineTerm;
    double _linearTerm;
    double _constantTerm;
    double _length;
};
