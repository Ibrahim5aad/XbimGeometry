/*
 * Geom2d_Clothoid.h
 *
 * Clothoid (Euler spiral / Cornu spiral) defined by a constant A.
 * Curvature varies linearly with arc length: kappa(s) = s / A^2.
 * Position is evaluated via Fresnel integrals (Simpson's rule), which
 * is more accurate for clothoids than the generic heading-based integration
 * in the Geom2d_Spiral base class.
 */

#pragma once

#include "Geom2d_Spiral.h"

#include <Geom_BSplineCurve.hxx>
#include <Standard_DefineHandle.hxx>

class Geom2d_Clothoid;
DEFINE_STANDARD_HANDLE(Geom2d_Clothoid, Geom2d_Spiral)

class Geom2d_Clothoid : public Geom2d_Spiral
{
public:
    Geom2d_Clothoid(const gp_Ax22d& placement, double clothoidConstant,
                    double startParam, double endParam);

    Standard_Real GetHeadingAt(Standard_Real s) const override;
    Standard_Real GetCurvatureAt(Standard_Real s) const override;

    /* Overrides base D0 with specialized Fresnel integral evaluation. */
    void D0(Standard_Real U, gp_Pnt2d& P) const override;

    /* B-spline conversion with tighter tolerance suited for clothoids. */
    Handle(Geom_BSplineCurve) ToBSplineClothoid(int numSamplePoints = 0) const;

    Handle(Geom2d_Geometry) Copy() const override;

private:
    double _clothoidConstant;

    /* Simpson's rule evaluation of Fresnel integrals C(t) and S(t). */
    void FresnelIntegrals(double t, double& C, double& S) const;

    /* Evaluate clothoid position at arc length s via Fresnel integrals. */
    void EvaluateClothoid(double s, double& x, double& y) const;
};
