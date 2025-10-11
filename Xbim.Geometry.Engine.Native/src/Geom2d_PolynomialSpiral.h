/*
 * Geom2d_PolynomialSpiral.h
 *
 * Polynomial spiral with curvature defined by a coefficient vector (A0..A7).
 * Handles 2nd, 3rd, and 7th order polynomial spirals used in IFC4x3
 * alignment geometry.
 *
 * Each coefficient An contributes a term to the curvature as a function of
 * arc length s. Heading (theta) is the analytical integral of curvature;
 * position is evaluated via the base class Simpson's rule integration.
 */

#pragma once

#include "Geom2d_Spiral.h"

#include <Standard_DefineHandle.hxx>

#include <vector>
#include <optional>

class Geom2d_PolynomialSpiral;
DEFINE_STANDARD_HANDLE(Geom2d_PolynomialSpiral, Geom2d_Spiral)

class Geom2d_PolynomialSpiral : public Geom2d_Spiral
{
public:
    Geom2d_PolynomialSpiral(const gp_Ax22d& placement,
                            const std::vector<std::optional<double>>& coefficients,
                            double startParam, double endParam);

    Standard_Real GetHeadingAt(Standard_Real s) const override;
    Standard_Real GetCurvatureAt(Standard_Real s) const override;

    Handle(Geom2d_Geometry) Copy() const override;

private:
    std::vector<std::optional<double>> _coefficients; // A0 to A7

    /* Analytical integral of curvature to get heading angle. */
    double CalculateTheta(double t) const;
};
