/*
 * Geom2d_PolynomialSpiral.cpp
 *
 * Polynomial spiral implementation. Curvature is defined by up to 8
 * coefficients (A0..A7); heading is their analytical integral. Position
 * evaluation uses the base class Simpson's rule integration.
 */

#include "Geom2d_PolynomialSpiral.h"

#include <cmath>

static inline double sign(double v) { return (v >= 0.0) ? 1.0 : -1.0; }

Geom2d_PolynomialSpiral::Geom2d_PolynomialSpiral(
    const gp_Ax22d& placement,
    const std::vector<std::optional<double>>& coefficients,
    double startParam, double endParam)
    : Geom2d_Spiral(placement, startParam, endParam)
    , _coefficients(coefficients)
{
    _coefficients.resize(8);
}

Standard_Real Geom2d_PolynomialSpiral::GetHeadingAt(Standard_Real s) const
{
    return CalculateTheta(s);
}

Standard_Real Geom2d_PolynomialSpiral::GetCurvatureAt(Standard_Real s) const
{
    // Curvature is the derivative of heading:
    // kappa(s) = sum over each coefficient term
    double kappa = 0.0;

    // A0 term: d/ds(t/A0) = 1/A0
    if (_coefficients[0].has_value())
        kappa += 1.0 / _coefficients[0].value();

    // A1 term: d/ds(sign(A1)*t^2/(2*A1^2)) = sign(A1)*t/A1^2
    if (_coefficients[1].has_value())
    {
        double A1 = _coefficients[1].value();
        kappa += sign(A1) * s / (A1 * A1);
    }

    // A2 term: d/ds(t^3/(3*A2^3)) = t^2/A2^3
    if (_coefficients[2].has_value())
    {
        double A2 = _coefficients[2].value();
        kappa += (s * s) / (A2 * A2 * A2);
    }

    // A3 term: d/ds(sign(A3)*t^4/(4*A3^4)) = sign(A3)*t^3/A3^4
    if (_coefficients[3].has_value())
    {
        double A3 = _coefficients[3].value();
        kappa += sign(A3) * std::pow(s, 3.0) / std::pow(A3, 4.0);
    }

    // A4 term: d/ds(t^5/(5*A4^5)) = t^4/A4^5
    if (_coefficients[4].has_value())
    {
        double A4 = _coefficients[4].value();
        kappa += std::pow(s, 4.0) / std::pow(A4, 5.0);
    }

    // A5 term: d/ds(sign(A5)*t^6/(6*A5^6)) = sign(A5)*t^5/A5^6
    if (_coefficients[5].has_value())
    {
        double A5 = _coefficients[5].value();
        kappa += sign(A5) * std::pow(s, 5.0) / std::pow(A5, 6.0);
    }

    // A6 term: d/ds(t^7/(7*A6^7)) = t^6/A6^7
    if (_coefficients[6].has_value())
    {
        double A6 = _coefficients[6].value();
        kappa += std::pow(s, 6.0) / std::pow(A6, 7.0);
    }

    // A7 term: d/ds(sign(A7)*t^8/(8*A7^8)) = sign(A7)*t^7/A7^8
    if (_coefficients[7].has_value())
    {
        double A7 = _coefficients[7].value();
        kappa += sign(A7) * std::pow(s, 7.0) / std::pow(A7, 8.0);
    }

    return kappa;
}

double Geom2d_PolynomialSpiral::CalculateTheta(double t) const
{
    double theta = 0.0;

    // A0 term: theta += t / A0
    if (_coefficients[0].has_value())
    {
        double A0 = _coefficients[0].value();
        theta += t / A0;
    }

    // A1 term: theta += sign(A1) * t^2 / (2 * A1^2)
    if (_coefficients[1].has_value())
    {
        double A1 = _coefficients[1].value();
        theta += sign(A1) * t * t / (2.0 * A1 * A1);
    }

    // A2 term: theta += t^3 / (3 * A2^3)
    if (_coefficients[2].has_value())
    {
        double A2 = _coefficients[2].value();
        theta += (t * t * t) / (3.0 * A2 * A2 * A2);
    }

    // A3 term: theta += sign(A3) * t^4 / (4 * A3^4)
    if (_coefficients[3].has_value())
    {
        double A3 = _coefficients[3].value();
        theta += sign(A3) * std::pow(t, 4.0) / (4.0 * std::pow(A3, 4.0));
    }

    // A4 term: theta += t^5 / (5 * A4^5)
    if (_coefficients[4].has_value())
    {
        double A4 = _coefficients[4].value();
        theta += std::pow(t, 5.0) / (5.0 * std::pow(A4, 5.0));
    }

    // A5 term: theta += sign(A5) * t^6 / (6 * A5^6)
    if (_coefficients[5].has_value())
    {
        double A5 = _coefficients[5].value();
        theta += sign(A5) * std::pow(t, 6.0) / (6.0 * std::pow(A5, 6.0));
    }

    // A6 term: theta += t^7 / (7 * A6^7)
    if (_coefficients[6].has_value())
    {
        double A6 = _coefficients[6].value();
        theta += std::pow(t, 7.0) / (7.0 * std::pow(A6, 7.0));
    }

    // A7 term: theta += sign(A7) * t^8 / (8 * A7^8)
    if (_coefficients[7].has_value())
    {
        double A7 = _coefficients[7].value();
        theta += sign(A7) * std::pow(t, 8.0) / (8.0 * std::pow(A7, 8.0));
    }

    return theta;
}

Handle(Geom2d_Geometry) Geom2d_PolynomialSpiral::Copy() const
{
    return new Geom2d_PolynomialSpiral(_placement, _coefficients, _startParam, _endParam);
}
