/*
 * Geom2d_Spiral.h
 *
 * Abstract base class for IFC4x3 spiral curves defined by curvature as a
 * function of arc length. Position is computed via Simpson's rule numerical
 * integration of the heading angle; tangent and normal derive from heading
 * and curvature respectively.
 *
 * Inherits Geom2d_BoundedCurve for integration with OCCT's 2D curve
 * infrastructure. Concrete subclasses (clothoid, sine spiral, cosine spiral,
 * polynomial spiral) provide GetHeadingAt() and GetCurvatureAt().
 */

#pragma once

#include <Geom2d_BoundedCurve.hxx>
#include <Geom_BSplineCurve.hxx>
#include <Standard_Type.hxx>
#include <Standard_DefineHandle.hxx>
#include <Standard_NotImplemented.hxx>
#include <gp_Pnt.hxx>
#include <gp_Pnt2d.hxx>
#include <gp_Vec2d.hxx>
#include <gp_Ax22d.hxx>
#include <gp_Trsf2d.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TColStd_Array1OfReal.hxx>
#include <GeomAPI_PointsToBSpline.hxx>
#include <Precision.hxx>

#include <cmath>
#include <algorithm>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

class Geom2d_Spiral;
DEFINE_STANDARD_HANDLE(Geom2d_Spiral, Geom2d_BoundedCurve)

class Geom2d_Spiral : public Geom2d_BoundedCurve
{
public:
    virtual ~Geom2d_Spiral() = default;

    /* Pure virtuals — concrete spirals must provide these. */
    virtual Standard_Real GetCurvatureAt(Standard_Real s) const = 0;
    virtual Standard_Real GetHeadingAt(Standard_Real s) const = 0;

    /* Placement coordinate system. */
    gp_Ax22d Placement() const { return _placement; }

    // ── Geom2d_Curve evaluation overrides ────────────────────────────────────

    /* D0: Position at parameter U via Simpson's rule integration of heading. */
    void D0(Standard_Real U, gp_Pnt2d& P) const override
    {
        double x, y;
        EvaluatePosition(U, x, y);
        P = ToPlacement(x, y);
    }

    /* D1: Position and first derivative (unit tangent from heading angle). */
    void D1(Standard_Real U, gp_Pnt2d& P, gp_Vec2d& V1) const override
    {
        D0(U, P);
        V1 = EvaluateTangent(U);
    }

    /* D2: Position, first and second derivatives (curvature normal). */
    void D2(Standard_Real U, gp_Pnt2d& P, gp_Vec2d& V1, gp_Vec2d& V2) const override
    {
        D1(U, P, V1);
        V2 = EvaluateNormal(U);
    }

    /* D3: Not implemented for numerically integrated spirals. */
    void D3(Standard_Real U, gp_Pnt2d& P, gp_Vec2d& V1, gp_Vec2d& V2,
            gp_Vec2d& V3) const override
    {
        D2(U, P, V1, V2);
        (void)V3;
        throw Standard_NotImplemented("Geom2d_Spiral::D3 not implemented");
    }

    /* DN: Arbitrary derivative order. Only orders 1 and 2 are supported. */
    gp_Vec2d DN(Standard_Real U, Standard_Integer N) const override
    {
        if (N == 1)
            return EvaluateTangent(U);
        if (N == 2)
            return EvaluateNormal(U);
        throw Standard_NotImplemented("Geom2d_Spiral::DN: only N=1,2 supported");
    }

    // ── Geom2d_BoundedCurve property overrides ──────────────────────────────

    Standard_Real FirstParameter() const override { return _startParam; }
    Standard_Real LastParameter() const override { return _endParam; }
    GeomAbs_Shape Continuity() const override { return GeomAbs_CN; }
    Standard_Boolean IsClosed() const override { return Standard_False; }
    Standard_Boolean IsPeriodic() const override { return Standard_False; }

    Standard_Boolean IsCN(Standard_Integer N) const override
    {
        return N <= 2;
    }

    gp_Pnt2d StartPoint() const override
    {
        gp_Pnt2d p;
        D0(_startParam, p);
        return p;
    }

    gp_Pnt2d EndPoint() const override
    {
        gp_Pnt2d p;
        D0(_endParam, p);
        return p;
    }

    // ── Reverse / Transform / Copy ──────────────────────────────────────────

    void Reverse() override
    {
        std::swap(_startParam, _endParam);
    }

    Standard_Real ReversedParameter(Standard_Real U) const override
    {
        return _startParam + _endParam - U;
    }

    void Transform(const gp_Trsf2d& T) override
    {
        _placement.Transform(T);
        UpdatePlacementTransform();
    }

    /* Copy — subclasses must return their concrete type. */
    Handle(Geom2d_Geometry) Copy() const override = 0;

    // ── B-spline conversion ────────────────────────────────────────────────

    /* Convert the spiral to a 3D B-spline approximation (Z=0) by sampling
     * points along the curve and fitting via GeomAPI_PointsToBSpline.
     * Returns null handle on fitting failure. */
    Handle(Geom_BSplineCurve) ToBSpline(int numSamplePoints = 0) const
    {
        if (numSamplePoints <= 0)
        {
            double span = std::abs(_endParam - _startParam);
            numSamplePoints = std::max(200, static_cast<int>(span * 2) + 1);
        }

        TColgp_Array1OfPnt points(1, numSamplePoints);
        TColStd_Array1OfReal params(1, numSamplePoints);

        for (int i = 0; i < numSamplePoints; i++)
        {
            double s = _startParam + (_endParam - _startParam) * i / (numSamplePoints - 1);
            gp_Pnt2d p2d;
            D0(s, p2d);
            points.SetValue(i + 1, gp_Pnt(p2d.X(), p2d.Y(), 0.0));
            params.SetValue(i + 1, s);
        }

        GeomAPI_PointsToBSpline fitter(points, params, 3, 8, GeomAbs_C2, 1.0e-10);
        if (!fitter.IsDone())
            return Handle(Geom_BSplineCurve)();

        return fitter.Curve();
    }

    // ── Accessors ───────────────────────────────────────────────────────────

    Standard_Real StartParam() const { return _startParam; }
    Standard_Real EndParam() const { return _endParam; }
    Standard_Integer GetIntegrationSteps() const { return _integrationSteps; }

protected:
    Geom2d_Spiral(const gp_Ax22d& placement, Standard_Real startParam, Standard_Real endParam)
        : _placement(placement)
        , _startParam(startParam)
        , _endParam(endParam)
    {
        UpdatePlacementTransform();

        double span = std::abs(endParam - startParam);
        _integrationSteps = std::max(1000, static_cast<int>(span));
        if (_integrationSteps % 2 != 0)
            _integrationSteps++;
    }

    Geom2d_Spiral() = default;

    gp_Ax22d _placement;
    gp_Trsf2d _placementTrsf;
    Standard_Real _startParam = 0.0;
    Standard_Real _endParam = 0.0;
    Standard_Integer _integrationSteps = 1000;

    // ── Shared evaluation helpers ───────────────────────────────────────────

    /*
     * EvaluatePosition — Simpson's rule integration of (cos(theta), sin(theta))
     * from _startParam to s, with theta0 subtracted to align the initial tangent
     * with the local X axis.
     */
    void EvaluatePosition(Standard_Real s, double& x, double& y) const
    {
        double theta0 = GetHeadingAt(_startParam);

        int N = _integrationSteps;
        double ds = (s - _startParam) / N;

        double sumX = 0.0, sumY = 0.0;

        for (int i = 0; i <= N; i++)
        {
            double t = _startParam + i * ds;
            double theta = GetHeadingAt(t) - theta0;

            double weight;
            if (i == 0 || i == N)
                weight = 1.0;
            else if (i % 2 != 0)
                weight = 4.0;
            else
                weight = 2.0;

            sumX += weight * std::cos(theta);
            sumY += weight * std::sin(theta);
        }

        x = (ds / 3.0) * sumX;
        y = (ds / 3.0) * sumY;
    }

    /*
     * EvaluateTangent — unit tangent vector at U from the heading angle,
     * transformed into the placement coordinate system.
     */
    gp_Vec2d EvaluateTangent(Standard_Real U) const
    {
        double theta0 = GetHeadingAt(_startParam);
        double theta = GetHeadingAt(U) - theta0;

        // Local tangent in intrinsic coordinates
        gp_Vec2d localTan(std::cos(theta), std::sin(theta));

        // Transform to placement coordinate system
        localTan.Transform(_placementTrsf);

        return localTan;
    }

    /*
     * EvaluateNormal — curvature normal: kappa * (-sin(theta), cos(theta)),
     * the derivative of the unit tangent with respect to arc length.
     */
    gp_Vec2d EvaluateNormal(Standard_Real U) const
    {
        double theta0 = GetHeadingAt(_startParam);
        double theta = GetHeadingAt(U) - theta0;
        double kappa = GetCurvatureAt(U);

        gp_Vec2d localNorm(-kappa * std::sin(theta), kappa * std::cos(theta));

        // Transform to placement coordinate system
        localNorm.Transform(_placementTrsf);

        return localNorm;
    }

    /* Transform a local 2D point into the placement coordinate system. */
    gp_Pnt2d ToPlacement(double localX, double localY) const
    {
        gp_Pnt2d p(localX, localY);
        p.Transform(_placementTrsf);
        return p;
    }

    /* Recompute _placementTrsf from current _placement. */
    void UpdatePlacementTransform()
    {
        gp_Trsf2d trsf;
        trsf.SetTransformation(_placement.XAxis());
        _placementTrsf = trsf.Inverted();
    }
};
