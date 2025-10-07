/*
 * xbim_spiral.h
 *
 * IFC4x3 spiral curve implementations: cosine spiral and polynomial
 * spirals. Each spiral defines curvature as a function of arc
 * length and evaluates points via numerical integration (Simpson's rule).
 *
 * All spirals inherit Geom2d_Spiral (which inherits Geom2d_BoundedCurve)
 * and can be converted to 3D B-spline approximations for use in the
 * geometry pipeline.
 */

#ifndef XBIM_SPIRAL_H
#define XBIM_SPIRAL_H

#include "Geom2d_Spiral.h"

#include <Geom_BSplineCurve.hxx>
#include <vector>
#include <optional>

/*
 * Spiral type enumeration for the generic polynomial spiral builder.
 */
enum XbimSpiralType
{
    XBIM_SPIRAL_CLOTHOID = 0,
    XBIM_SPIRAL_SINE = 1,
    XBIM_SPIRAL_COSINE = 2,
    XBIM_SPIRAL_POLYNOMIAL = 3
};

/*
 * Cosine spiral.
 * Curvature: kappa(s) = L/C0 + (L/Ct)*cos(pi*s/L)
 */
class XbimCosineSpiral : public Geom2d_Spiral
{
public:
    XbimCosineSpiral(const gp_Ax22d& placement,
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

/*
 * Polynomial spiral (handles 2nd, 3rd, and 7th order polynomial spirals).
 * Curvature defined by up to 8 coefficients (A0 through A7).
 * Each coefficient contributes a term to the curvature as a function of arc length.
 */
class XbimPolynomialSpiral : public Geom2d_Spiral
{
public:
    XbimPolynomialSpiral(const gp_Ax22d& placement,
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

#endif /* XBIM_SPIRAL_H */
