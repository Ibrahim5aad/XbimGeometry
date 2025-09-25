/*
 * xbim_spiral.h
 *
 * IFC4x3 spiral curve implementations: clothoid, sine spiral, cosine spiral,
 * and polynomial spirals. Each spiral defines curvature as a function of arc
 * length and evaluates points via numerical integration (Simpson's rule).
 *
 * All spirals are 2D parametric curves that get converted to 3D B-spline
 * approximations for use in the geometry pipeline.
 */

#ifndef XBIM_SPIRAL_H
#define XBIM_SPIRAL_H

#include <gp_Ax22d.hxx>
#include <gp_Pnt2d.hxx>
#include <gp_Trsf2d.hxx>
#include <Geom_BSplineCurve.hxx>

#include <vector>
#include <optional>
#include <cmath>

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
 * Base class for all IFC4x3 spiral implementations.
 * Spirals define curvature as a function of arc length parameter s,
 * and compute positions by numerically integrating the heading angle.
 */
class XbimSpiral
{
public:
    virtual ~XbimSpiral() = default;

    /* Evaluate the heading angle at arc length s (integral of curvature). */
    virtual double GetHeadingAt(double s) const = 0;

    /* Evaluate the 2D point at arc length s in local coordinates. */
    void Evaluate(double s, double& x, double& y) const;

    /* Convert the spiral to a 3D B-spline approximation. */
    Handle(Geom_BSplineCurve) ToBSpline(int numSamplePoints = 0) const;

    double StartParam() const { return _startParam; }
    double EndParam() const { return _endParam; }

protected:
    XbimSpiral(const gp_Ax22d& placement, double startParam, double endParam);

    gp_Ax22d _placement;
    gp_Trsf2d _placementTrsf;
    double _startParam;
    double _endParam;

    /* Number of Simpson's rule integration steps. */
    int _integrationSteps;

    /* Transform a local 2D point into the placement coordinate system. */
    gp_Pnt2d ToPlacement(double localX, double localY) const;
};

/*
 * Clothoid (Euler spiral / Cornu spiral).
 * Curvature varies linearly with arc length: kappa(s) = s / A^2
 * Uses Fresnel integral evaluation via Simpson's rule.
 */
class XbimClothoid : public XbimSpiral
{
public:
    XbimClothoid(const gp_Ax22d& placement, double clothoidConstant,
                 double startParam, double endParam);

    double GetHeadingAt(double s) const override;

    /* Clothoid uses specialized Fresnel integral evaluation. */
    void EvaluateClothoid(double s, double& x, double& y) const;

    Handle(Geom_BSplineCurve) ToBSplineClothoid(int numSamplePoints = 0) const;

private:
    double _clothoidConstant;

    void FresnelIntegrals(double t, double& C, double& S) const;
};

/*
 * Sine spiral.
 * Curvature: kappa(s) = L/C0 + sign(L1)*(L/L1)^2*(s/L) + (L/S)*sin(2*pi*s/L)
 * where L = arc length span, C0 = constantTerm, L1 = linearTerm, S = sineTerm
 */
class XbimSineSpiral : public XbimSpiral
{
public:
    XbimSineSpiral(const gp_Ax22d& placement,
                   double sineTerm, double linearTerm, double constantTerm,
                   double startParam, double endParam);

    double GetHeadingAt(double s) const override;

private:
    double _sineTerm;
    double _linearTerm;
    double _constantTerm;
    double _length;
};

/*
 * Cosine spiral.
 * Curvature: kappa(s) = L/C0 + (L/Ct)*cos(pi*s/L)
 */
class XbimCosineSpiral : public XbimSpiral
{
public:
    XbimCosineSpiral(const gp_Ax22d& placement,
                     double cosineTerm, double constantTerm,
                     double startParam, double endParam);

    double GetHeadingAt(double s) const override;

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
class XbimPolynomialSpiral : public XbimSpiral
{
public:
    XbimPolynomialSpiral(const gp_Ax22d& placement,
                         const std::vector<std::optional<double>>& coefficients,
                         double startParam, double endParam);

    double GetHeadingAt(double s) const override;

private:
    std::vector<std::optional<double>> _coefficients; // A0 to A7

    /* Analytical integral of curvature to get heading angle. */
    double CalculateTheta(double t) const;
};

#endif /* XBIM_SPIRAL_H */
