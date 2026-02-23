/*
 * Geom2d_SineSpiral.cpp
 *
 * Sine spiral implementation. Curvature varies sinusoidally with arc length;
 * heading is the analytical integral of curvature. Position evaluation uses
 * the base class Simpson's rule integration of the heading angle.
 */

#include "Geom2d_SineSpiral.h"

#include <cmath>

static inline double sign(double v) { return (v >= 0.0) ? 1.0 : -1.0; }

Geom2d_SineSpiral::Geom2d_SineSpiral(const gp_Ax22d& placement,
                                       double sineTerm, double linearTerm, double constantTerm,
                                       double startParam, double endParam)
    : Geom2d_Spiral(placement, startParam, endParam)
    , _sineTerm(sineTerm)
    , _linearTerm(linearTerm)
    , _constantTerm(constantTerm)
    , _length(endParam - startParam)
{
}

Standard_Real Geom2d_SineSpiral::GetHeadingAt(Standard_Real s) const
{
    // Heading angle = integral of curvature from 0 to s
    // theta(s) = s/C0 + 0.5*sign(L1)*(s/L1)^2 + (-L/(2*pi*S))*(cos(2*pi*s/L) - 1)

    double firstTerm = 0.0;
    if (_constantTerm != 0.0)
        firstTerm = s / _constantTerm;

    double secondTerm = 0.0;
    if (_linearTerm != 0.0)
        secondTerm = 0.5 * sign(_linearTerm) * std::pow(s / _linearTerm, 2.0);

    double thirdTerm = -1.0 * (_length / (2.0 * M_PI * _sineTerm)) *
                       (std::cos(2.0 * M_PI * s / _length) - 1.0);

    return firstTerm + secondTerm + thirdTerm;
}

Standard_Real Geom2d_SineSpiral::GetCurvatureAt(Standard_Real s) const
{
    // kappa(s) = 1/C0 + sign(L1)/L1^2 * s + (1/S) * sin(2*pi*s/L)
    // This is the derivative of the heading angle with respect to s.

    double firstTerm = 0.0;
    if (_constantTerm != 0.0)
        firstTerm = 1.0 / _constantTerm;

    double secondTerm = 0.0;
    if (_linearTerm != 0.0)
        secondTerm = sign(_linearTerm) * s / (_linearTerm * _linearTerm);

    double thirdTerm = (1.0 / _sineTerm) * std::sin(2.0 * M_PI * s / _length);

    return firstTerm + secondTerm + thirdTerm;
}

Handle(Geom2d_Geometry) Geom2d_SineSpiral::Copy() const
{
    return new Geom2d_SineSpiral(_placement, _sineTerm, _linearTerm, _constantTerm,
                                  _startParam, _endParam);
}
