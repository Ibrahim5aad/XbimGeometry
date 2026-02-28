/*
 * Geom2d_Polynomial.h
 *
 * A 2D polynomial curve defined by separate X and Y coefficient vectors:
 *   X(u) = sum(coeffX[i] * u^i)
 *   Y(u) = sum(coeffY[i] * u^i)
 *
 * Inherits Geom2d_BoundedCurve for integration with OCCT's 2D curve infrastructure.
 * Supports D0 through D3 evaluation via polynomial differentiation (Horner's method).
 *
 */

#pragma once

#include <Geom2d_BoundedCurve.hxx>
#include <Standard_Type.hxx>
#include <Standard_DefineHandle.hxx>
#include <Standard_DomainError.hxx>
#include <gp_Pnt2d.hxx>
#include <gp_Vec2d.hxx>
#include <gp_Ax22d.hxx>
#include <gp_Trsf2d.hxx>
#include <Precision.hxx>

#include <vector>
#include <algorithm>
#include <cmath>

class Geom2d_Polynomial;
DEFINE_STANDARD_HANDLE(Geom2d_Polynomial, Geom2d_BoundedCurve)

class Geom2d_Polynomial : public Geom2d_BoundedCurve
{
public:
    Geom2d_Polynomial(
        const gp_Ax22d& placement,
        const std::vector<Standard_Real>& coeffX,
        const std::vector<Standard_Real>& coeffY,
        Standard_Real firstParam = 0.0,
        Standard_Real lastParam = 1.0);

    // Evaluation
    void D0(Standard_Real U, gp_Pnt2d& P) const override;
    void D1(Standard_Real U, gp_Pnt2d& P, gp_Vec2d& V1) const override;
    void D2(Standard_Real U, gp_Pnt2d& P, gp_Vec2d& V1, gp_Vec2d& V2) const override;
    void D3(Standard_Real U, gp_Pnt2d& P, gp_Vec2d& V1, gp_Vec2d& V2, gp_Vec2d& V3) const override;
    gp_Vec2d DN(Standard_Real U, Standard_Integer N) const override;

    // Curvature
    Standard_Real GetCurvatureAt(Standard_Real s) const;

    // Properties
    gp_Ax22d Placement() const { return _placement; }
    Standard_Real FirstParameter() const override { return _firstParam; }
    Standard_Real LastParameter() const override { return _lastParam; }
    GeomAbs_Shape Continuity() const override { return GeomAbs_CN; }
    Standard_Boolean IsClosed() const override;
    Standard_Boolean IsPeriodic() const override { return Standard_False; }
    Standard_Boolean IsCN(Standard_Integer N) const override;

    // Bounded curve interface
    gp_Pnt2d StartPoint() const override;
    gp_Pnt2d EndPoint() const override;

    // Reverse / Transform / Copy
    void Reverse() override;
    Standard_Real ReversedParameter(Standard_Real U) const override;
    void Transform(const gp_Trsf2d& T) override;
    Handle(Geom2d_Geometry) Copy() const override;

    // Accessors
    const std::vector<Standard_Real>& CoefficientsX() const { return _coeffX; }
    const std::vector<Standard_Real>& CoefficientsY() const { return _coeffY; }

private:
    gp_Ax22d _placement;
    std::vector<Standard_Real> _coeffX;
    std::vector<Standard_Real> _coeffY;
    Standard_Real _firstParam;
    Standard_Real _lastParam;
    gp_Trsf2d _placementTrsf;

    // Polynomial evaluation via Horner's method
    static Standard_Real EvaluatePolynomial(
        const std::vector<Standard_Real>& coeffs, Standard_Real t);
    static Standard_Real EvaluatePolynomialDerivative(
        const std::vector<Standard_Real>& coeffs, Standard_Real s);
    static Standard_Real EvaluatePolynomialSecondDerivative(
        const std::vector<Standard_Real>& coeffs, Standard_Real s);
    static Standard_Real EvaluatePolynomialThirdDerivative(
        const std::vector<Standard_Real>& coeffs, Standard_Real s);
    static Standard_Real EvaluatePolynomialNthDerivative(
        const std::vector<Standard_Real>& coeffs, Standard_Real s, Standard_Integer N);

    static void ReverseCoefficients(std::vector<Standard_Real>& coeffs);
};
