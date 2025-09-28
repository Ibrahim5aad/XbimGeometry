/*
 * Geom2d_Polynomial.cpp
 *
 * Implements the Geom2d_Polynomial bounded curve class.
 * Evaluates parametric polynomial curves X(u), Y(u) using Horner's method
 * and supports derivatives up to arbitrary order.
 */

#include "Geom2d_Polynomial.h"

Geom2d_Polynomial::Geom2d_Polynomial(
    const gp_Ax22d& placement,
    const std::vector<Standard_Real>& coeffX,
    const std::vector<Standard_Real>& coeffY,
    Standard_Real firstParam,
    Standard_Real lastParam)
    : _placement(placement)
    , _coeffX(coeffX)
    , _coeffY(coeffY)
    , _firstParam(firstParam)
    , _lastParam(lastParam)
{
    if (_lastParam <= _firstParam)
        throw Standard_DomainError("lastParam must be greater than firstParam.");

    gp_Trsf2d transform;
    transform.SetTransformation(_placement.XAxis());
    transform.Invert();
    _placementTrsf = transform;
}

#pragma region Evaluation

void Geom2d_Polynomial::D0(Standard_Real U, gp_Pnt2d& P) const
{
    U = std::clamp(U, _firstParam, _lastParam);
    Standard_Real x = EvaluatePolynomial(_coeffX, U);
    Standard_Real y = EvaluatePolynomial(_coeffY, U);
    P.SetCoord(x, y);
    P.Transform(_placementTrsf);
}

void Geom2d_Polynomial::D1(Standard_Real U, gp_Pnt2d& P, gp_Vec2d& V1) const
{
    U = std::clamp(U, _firstParam, _lastParam);
    Standard_Real x = EvaluatePolynomial(_coeffX, U);
    Standard_Real y = EvaluatePolynomial(_coeffY, U);
    Standard_Real dx = EvaluatePolynomialDerivative(_coeffX, U);
    Standard_Real dy = EvaluatePolynomialDerivative(_coeffY, U);
    P.SetCoord(x, y);
    V1.SetCoord(dx, dy);
    P.Transform(_placementTrsf);
    V1.Transform(_placementTrsf);
}

void Geom2d_Polynomial::D2(Standard_Real U, gp_Pnt2d& P, gp_Vec2d& V1, gp_Vec2d& V2) const
{
    U = std::clamp(U, _firstParam, _lastParam);
    Standard_Real x = EvaluatePolynomial(_coeffX, U);
    Standard_Real y = EvaluatePolynomial(_coeffY, U);
    Standard_Real dx = EvaluatePolynomialDerivative(_coeffX, U);
    Standard_Real dy = EvaluatePolynomialDerivative(_coeffY, U);
    Standard_Real ddx = EvaluatePolynomialSecondDerivative(_coeffX, U);
    Standard_Real ddy = EvaluatePolynomialSecondDerivative(_coeffY, U);
    P.SetCoord(x, y);
    V1.SetCoord(dx, dy);
    V2.SetCoord(ddx, ddy);
    P.Transform(_placementTrsf);
    V1.Transform(_placementTrsf);
    V2.Transform(_placementTrsf);
}

void Geom2d_Polynomial::D3(Standard_Real U, gp_Pnt2d& P, gp_Vec2d& V1, gp_Vec2d& V2, gp_Vec2d& V3) const
{
    U = std::clamp(U, _firstParam, _lastParam);
    Standard_Real x = EvaluatePolynomial(_coeffX, U);
    Standard_Real y = EvaluatePolynomial(_coeffY, U);
    Standard_Real dx = EvaluatePolynomialDerivative(_coeffX, U);
    Standard_Real dy = EvaluatePolynomialDerivative(_coeffY, U);
    Standard_Real ddx = EvaluatePolynomialSecondDerivative(_coeffX, U);
    Standard_Real ddy = EvaluatePolynomialSecondDerivative(_coeffY, U);
    Standard_Real dddx = EvaluatePolynomialThirdDerivative(_coeffX, U);
    Standard_Real dddy = EvaluatePolynomialThirdDerivative(_coeffY, U);
    P.SetCoord(x, y);
    V1.SetCoord(dx, dy);
    V2.SetCoord(ddx, ddy);
    V3.SetCoord(dddx, dddy);
    P.Transform(_placementTrsf);
    V1.Transform(_placementTrsf);
    // Note: legacy code omits V2 transform in D3 — we replicate that behavior
    V3.Transform(_placementTrsf);
}

gp_Vec2d Geom2d_Polynomial::DN(Standard_Real U, Standard_Integer N) const
{
    U = std::clamp(U, _firstParam, _lastParam);
    if (N < 1)
        throw Standard_DomainError("Derivative order must be at least 1.");

    Standard_Real dx = 0.0;
    Standard_Real dy = 0.0;

    if (N == 1)
    {
        dx = EvaluatePolynomialDerivative(_coeffX, U);
        dy = EvaluatePolynomialDerivative(_coeffY, U);
    }
    else if (N == 2)
    {
        dx = EvaluatePolynomialSecondDerivative(_coeffX, U);
        dy = EvaluatePolynomialSecondDerivative(_coeffY, U);
    }
    else if (N == 3)
    {
        dx = EvaluatePolynomialThirdDerivative(_coeffX, U);
        dy = EvaluatePolynomialThirdDerivative(_coeffY, U);
    }
    else
    {
        dx = EvaluatePolynomialNthDerivative(_coeffX, U, N);
        dy = EvaluatePolynomialNthDerivative(_coeffY, U, N);
    }

    return gp_Vec2d(dx, dy);
}

#pragma endregion

#pragma region Curvature

Standard_Real Geom2d_Polynomial::GetCurvatureAt(Standard_Real s) const
{
    Standard_Real dx = EvaluatePolynomialDerivative(_coeffX, s);
    Standard_Real dy = EvaluatePolynomialDerivative(_coeffY, s);
    Standard_Real ddx = EvaluatePolynomialSecondDerivative(_coeffX, s);
    Standard_Real ddy = EvaluatePolynomialSecondDerivative(_coeffY, s);

    Standard_Real numerator = dx * ddy - dy * ddx;
    Standard_Real denominator = std::pow(dx * dx + dy * dy, 1.5);

    if (denominator == 0.0)
        throw Standard_DomainError("Denominator in curvature computation is zero.");

    return numerator / denominator;
}

#pragma endregion

#pragma region Properties

Standard_Boolean Geom2d_Polynomial::IsClosed() const
{
    gp_Pnt2d pStart, pEnd;
    D0(_firstParam, pStart);
    D0(_lastParam, pEnd);
    return pStart.IsEqual(pEnd, Precision::Confusion());
}

Standard_Boolean Geom2d_Polynomial::IsCN(Standard_Integer /*N*/) const
{
    return Standard_True;
}

gp_Pnt2d Geom2d_Polynomial::StartPoint() const
{
    gp_Pnt2d P;
    D0(_firstParam, P);
    return P;
}

gp_Pnt2d Geom2d_Polynomial::EndPoint() const
{
    gp_Pnt2d P;
    D0(_lastParam, P);
    return P;
}

#pragma endregion

#pragma region Reverse / Transform / Copy

void Geom2d_Polynomial::Reverse()
{
    Standard_Real temp = _firstParam;
    _firstParam = -_lastParam;
    _lastParam = -temp;
    ReverseCoefficients(_coeffX);
    ReverseCoefficients(_coeffY);
}

Standard_Real Geom2d_Polynomial::ReversedParameter(Standard_Real U) const
{
    return _firstParam + _lastParam - U;
}

void Geom2d_Polynomial::Transform(const gp_Trsf2d& T)
{
    _placement.Transform(T);
    gp_Trsf2d transform;
    transform.SetTransformation(_placement.XAxis());
    transform.Invert();
    _placementTrsf = transform;
}

Handle(Geom2d_Geometry) Geom2d_Polynomial::Copy() const
{
    return new Geom2d_Polynomial(_placement, _coeffX, _coeffY, _firstParam, _lastParam);
}

#pragma endregion

#pragma region Polynomial Evaluation

Standard_Real Geom2d_Polynomial::EvaluatePolynomial(
    const std::vector<Standard_Real>& coeffs, Standard_Real t)
{
    // Horner's method: iterate from highest to lowest coefficient
    Standard_Real value = 0.0;
    for (auto it = coeffs.rbegin(); it != coeffs.rend(); ++it)
        value = value * t + *it;
    return value;
}

Standard_Real Geom2d_Polynomial::EvaluatePolynomialDerivative(
    const std::vector<Standard_Real>& coeffs, Standard_Real s)
{
    Standard_Real value = 0.0;
    Standard_Integer degree = static_cast<Standard_Integer>(coeffs.size()) - 1;
    for (Standard_Integer i = degree; i >= 1; --i)
        value = value * s + i * coeffs[i];
    return value;
}

Standard_Real Geom2d_Polynomial::EvaluatePolynomialSecondDerivative(
    const std::vector<Standard_Real>& coeffs, Standard_Real s)
{
    Standard_Real value = 0.0;
    Standard_Integer degree = static_cast<Standard_Integer>(coeffs.size()) - 1;
    for (Standard_Integer i = degree; i >= 2; --i)
        value = value * s + i * (i - 1) * coeffs[i];
    return value;
}

Standard_Real Geom2d_Polynomial::EvaluatePolynomialThirdDerivative(
    const std::vector<Standard_Real>& coeffs, Standard_Real s)
{
    Standard_Real value = 0.0;
    Standard_Integer degree = static_cast<Standard_Integer>(coeffs.size()) - 1;
    for (Standard_Integer i = degree; i >= 3; --i)
        value = value * s + i * (i - 1) * (i - 2) * coeffs[i];
    return value;
}

Standard_Real Geom2d_Polynomial::EvaluatePolynomialNthDerivative(
    const std::vector<Standard_Real>& coeffs, Standard_Real s, Standard_Integer N)
{
    if (N < 0)
        throw Standard_DomainError("Derivative order must be non-negative.");
    if (N >= static_cast<Standard_Integer>(coeffs.size()))
        return 0.0;

    Standard_Real value = 0.0;
    Standard_Integer degree = static_cast<Standard_Integer>(coeffs.size()) - 1;
    for (Standard_Integer i = degree; i >= N; --i)
    {
        Standard_Real coeff = coeffs[i];
        for (Standard_Integer k = 0; k < N; ++k)
            coeff *= (i - k);
        value = value * s + coeff;
    }
    return value;
}

void Geom2d_Polynomial::ReverseCoefficients(std::vector<Standard_Real>& coeffs)
{
    std::vector<Standard_Real> reversed(coeffs.size(), 0.0);
    Standard_Integer degree = static_cast<Standard_Integer>(coeffs.size()) - 1;
    for (Standard_Integer i = 0; i <= degree; ++i)
        reversed[degree - i] = coeffs[i] * ((i % 2 == 0) ? 1.0 : -1.0);
    coeffs = reversed;
}

#pragma endregion
