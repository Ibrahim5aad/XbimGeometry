#include "Geom2d_CosineSpiral.h"

#include <cmath>

Geom2d_CosineSpiral::Geom2d_CosineSpiral(const gp_Ax22d& placement,
                                           double cosineTerm, double constantTerm,
                                           double startParam, double endParam)
    : Geom2d_Spiral(placement, startParam, endParam)
    , _cosineTerm(cosineTerm)
    , _constantTerm(constantTerm)
    , _length(endParam - startParam)
{
}

Standard_Real Geom2d_CosineSpiral::GetHeadingAt(Standard_Real s) const
{
    // Heading angle = integral of curvature from 0 to s
    // theta(s) = s/C0 + (L/(pi*Ct))*sin(pi*s/L)

    double firstTerm = 0.0;
    if (_constantTerm != 0.0)
        firstTerm = s / _constantTerm;

    double secondTerm = (_length / (M_PI * _cosineTerm)) * std::sin(M_PI * s / _length);

    return firstTerm + secondTerm;
}

Standard_Real Geom2d_CosineSpiral::GetCurvatureAt(Standard_Real s) const
{
    // kappa(s) = 1/C0 + (1/Ct)*cos(pi*s/L)
    // Derivative of heading with respect to s.

    double firstTerm = 0.0;
    if (_constantTerm != 0.0)
        firstTerm = 1.0 / _constantTerm;

    double secondTerm = (1.0 / _cosineTerm) * std::cos(M_PI * s / _length);

    return firstTerm + secondTerm;
}

Handle(Geom2d_Geometry) Geom2d_CosineSpiral::Copy() const
{
    return new Geom2d_CosineSpiral(_placement, _cosineTerm, _constantTerm,
                                    _startParam, _endParam);
}
