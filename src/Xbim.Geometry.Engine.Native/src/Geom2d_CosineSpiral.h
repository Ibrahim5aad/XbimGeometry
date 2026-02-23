/*
 * Geom2d_CosineSpiral.h
 *
 * Cosine spiral curve for IFC4x3 alignment geometry. Curvature is defined
 * by a cosinusoidal function of arc length:
 *   kappa(s) = 1/C0 + (1/Ct)*cos(pi*s/L)
 * where L = arc length span, C0 = constant term, Ct = cosine term.
 *
 * Heading is the analytical integral of curvature. Position is computed
 * via the base class Simpson's rule integration of the heading angle.
 */

#pragma once

#include "Geom2d_Spiral.h"

#include <Standard_DefineHandle.hxx>

class Geom2d_CosineSpiral;
DEFINE_STANDARD_HANDLE(Geom2d_CosineSpiral, Geom2d_Spiral)

class Geom2d_CosineSpiral : public Geom2d_Spiral
{
public:
    Geom2d_CosineSpiral(const gp_Ax22d& placement,
                        double cosineTerm, double constantTerm,
                        double startParam, double endParam);

    Standard_Real GetHeadingAt(Standard_Real s) const override;
    Standard_Real GetCurvatureAt(Standard_Real s) const override;

    Handle(Geom2d_Geometry) Copy() const override;

private:
    double _cosineTerm;
    double _constantTerm;
    double _length;
};
