/*
 * Geom_GradientCurve.h
 *
 * 3D curve combining a horizontal 2D projection curve with a height function
 * (also 2D) to produce a terrain-following alignment curve:
 *   P(u) = (horizontal.X(u), horizontal.Y(u), heightFunction.Y(u))
 *
 * Supports D0 through D3 derivative evaluation and B-spline conversion via
 * point sampling. Used for road/railway vertical profiles (IfcGradientCurve).
 */

#pragma once

#include "Geom_ConvertibleToBSpline.h"

#include <Geom2d_Curve.hxx>
#include <Geom2dAdaptor_Curve.hxx>
#include <GCPnts_UniformAbscissa.hxx>
#include <GeomAPI_PointsToBSpline.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <Standard_NotImplemented.hxx>
#include <Precision.hxx>

#include <algorithm>

class Geom_GradientCurve;
DEFINE_STANDARD_HANDLE(Geom_GradientCurve, Geom_ConvertibleToBSpline)

class Geom_GradientCurve : public Geom_ConvertibleToBSpline
{
public:
    Geom_GradientCurve(
        const Handle(Geom2d_Curve)& horizontalCurve,
        const Handle(Geom2d_Curve)& heightFunction)
        : _horizontalCurve(horizontalCurve),
          _heightFunction(heightFunction)
    {
        _firstParam = 0.0;
        _lastParam = _horizontalCurve->LastParameter()
                   - _horizontalCurve->FirstParameter();
    }

    /* ---- Evaluation ---- */

    void D0(Standard_Real U, gp_Pnt& P) const override
    {
        U = Clamp(U);
        Standard_Real hU = U + _horizontalCurve->FirstParameter();
        Standard_Real vU = U + _heightFunction->FirstParameter();

        gp_Pnt2d hPt, vPt;
        _horizontalCurve->D0(hU, hPt);
        _heightFunction->D0(vU, vPt);

        P.SetCoord(hPt.X(), hPt.Y(), vPt.Y());
    }

    void D1(Standard_Real U, gp_Pnt& P, gp_Vec& V1) const override
    {
        U = Clamp(U);
        Standard_Real hU = U + _horizontalCurve->FirstParameter();
        Standard_Real vU = U + _heightFunction->FirstParameter();

        gp_Pnt2d hPt, vPt;
        gp_Vec2d hV, vV;
        _horizontalCurve->D1(hU, hPt, hV);
        _heightFunction->D1(vU, vPt, vV);

        P.SetCoord(hPt.X(), hPt.Y(), vPt.Y());
        V1.SetCoord(hV.X(), hV.Y(), vV.Y());
    }

    void D2(Standard_Real U, gp_Pnt& P, gp_Vec& V1, gp_Vec& V2) const override
    {
        U = Clamp(U);
        Standard_Real hU = U + _horizontalCurve->FirstParameter();
        Standard_Real vU = U + _heightFunction->FirstParameter();

        gp_Pnt2d hPt, vPt;
        gp_Vec2d hV1, hV2, vV1, vV2;
        _horizontalCurve->D2(hU, hPt, hV1, hV2);
        _heightFunction->D2(vU, vPt, vV1, vV2);

        P.SetCoord(hPt.X(), hPt.Y(), vPt.Y());
        V1.SetCoord(hV1.X(), hV1.Y(), vV1.Y());
        V2.SetCoord(hV2.X(), hV2.Y(), vV2.Y());
    }

    void D3(Standard_Real U, gp_Pnt& P,
            gp_Vec& V1, gp_Vec& V2, gp_Vec& V3) const override
    {
        U = Clamp(U);
        Standard_Real hU = U + _horizontalCurve->FirstParameter();
        Standard_Real vU = U + _heightFunction->FirstParameter();

        gp_Pnt2d hPt, vPt;
        gp_Vec2d hV1, hV2, hV3, vV1, vV2, vV3;
        _horizontalCurve->D3(hU, hPt, hV1, hV2, hV3);
        _heightFunction->D3(vU, vPt, vV1, vV2, vV3);

        P.SetCoord(hPt.X(), hPt.Y(), vPt.Y());
        V1.SetCoord(hV1.X(), hV1.Y(), vV1.Y());
        V2.SetCoord(hV2.X(), hV2.Y(), vV2.Y());
        V3.SetCoord(hV3.X(), hV3.Y(), vV3.Y());
    }

    gp_Vec DN(Standard_Real U, Standard_Integer N) const override
    {
        if (N == 1) {
            gp_Pnt P; gp_Vec V1;
            D1(U, P, V1);
            return V1;
        }
        if (N == 2) {
            gp_Pnt P; gp_Vec V1, V2;
            D2(U, P, V1, V2);
            return V2;
        }
        if (N == 3) {
            gp_Pnt P; gp_Vec V1, V2, V3;
            D3(U, P, V1, V2, V3);
            return V3;
        }
        throw Standard_NotImplemented("Geom_GradientCurve::DN not implemented for N > 3");
    }

    /* ---- Curve properties ---- */

    Standard_Real FirstParameter() const override { return _firstParam; }
    Standard_Real LastParameter()  const override { return _lastParam; }

    GeomAbs_Shape Continuity() const override
    {
        return _horizontalCurve->Continuity();
    }

    Standard_Boolean IsClosed() const override
    {
        gp_Pnt ps, pe;
        D0(FirstParameter(), ps);
        D0(LastParameter(), pe);
        return ps.IsEqual(pe, Precision::Confusion());
    }

    Standard_Boolean IsPeriodic() const override
    {
        return _horizontalCurve->IsPeriodic();
    }

    Standard_Boolean IsCN(Standard_Integer N) const override
    {
        return _horizontalCurve->IsCN(N);
    }

    void Reverse() override
    {
        _horizontalCurve->Reverse();
        _heightFunction->Reverse();
        Standard_Real temp = _firstParam;
        _firstParam = -_lastParam;
        _lastParam  = -temp;
    }

    Standard_Real ReversedParameter(Standard_Real U) const override
    {
        return _firstParam + _lastParam - U;
    }

    void Transform(const gp_Trsf& /*T*/) override
    {
        throw Standard_NotImplemented("Geom_GradientCurve::Transform not implemented");
    }

    Handle(Geom_Geometry) Copy() const override
    {
        return Clone();
    }

    Handle(Geom_GradientCurve) Clone() const
    {
        Handle(Geom2d_Curve) hCopy =
            Handle(Geom2d_Curve)::DownCast(_horizontalCurve->Copy());
        Handle(Geom2d_Curve) vCopy =
            Handle(Geom2d_Curve)::DownCast(_heightFunction->Copy());
        return new Geom_GradientCurve(hCopy, vCopy);
    }

    /* ---- ToBSpline ---- */

    Handle(Geom_BSplineCurve) ToBSpline(int nbPoints) const override
    {
        TColgp_Array1OfPnt points(1, nbPoints);
        SampleByArcLength(points, nbPoints);
        GeomAPI_PointsToBSpline fitter(points, 8, 8, GeomAbs_CN);
        return fitter.Curve();
    }

    Handle(Geom_BSplineCurve) ToBSpline(
        double startParam, double endParam, int nbPoints) const override
    {
        TColgp_Array1OfPnt points(1, nbPoints);
        SampleUniform(points, nbPoints, startParam, endParam);
        GeomAPI_PointsToBSpline fitter(points);
        return fitter.Curve();
    }

    /* ---- Accessors ---- */

    const Handle(Geom2d_Curve)& HorizontalCurve() const { return _horizontalCurve; }
    const Handle(Geom2d_Curve)& HeightFunction()   const { return _heightFunction; }

private:
    Handle(Geom2d_Curve) _horizontalCurve;
    Handle(Geom2d_Curve) _heightFunction;
    Standard_Real _firstParam;
    Standard_Real _lastParam;

    Standard_Real Clamp(Standard_Real U) const
    {
        if (U < _firstParam) return _firstParam;
        if (U > _lastParam)  return _lastParam;
        return U;
    }

    /*
     * Sample points using uniform abscissa (arc-length spacing) on the
     * horizontal projection, matching the legacy ToBSpline(int nbPoints).
     */
    void SampleByArcLength(TColgp_Array1OfPnt& points, int nbPoints) const
    {
        Standard_Real p1 = _horizontalCurve->FirstParameter();
        Standard_Real p2 = _horizontalCurve->LastParameter();

        Geom2dAdaptor_Curve adaptor(_horizontalCurve, p1, p2);
        GCPnts_UniformAbscissa sampler(adaptor, nbPoints);

        for (Standard_Integer i = 1; i <= nbPoints; ++i)
        {
            Standard_Real param = sampler.Parameter(i);
            gp_Pnt2d hPt;
            _horizontalCurve->D0(param, hPt);
            gp_Pnt2d vPt;
            _heightFunction->D0(param, vPt);
            points.SetValue(i, gp_Pnt(hPt.X(), hPt.Y(), vPt.Y()));
        }
    }

    /*
     * Sample points at uniform parameter intervals in [startParam, endParam].
     */
    void SampleUniform(TColgp_Array1OfPnt& points, int nbPoints,
                       double startParam, double endParam) const
    {
        if (nbPoints < 2) nbPoints = 2;
        double delta = (endParam - startParam) / (nbPoints - 1);

        for (Standard_Integer i = 1; i <= nbPoints; ++i)
        {
            double U = startParam + (i - 1) * delta;
            if (U > endParam) U = endParam;
            gp_Pnt P;
            D0(U, P);
            points.SetValue(i, P);
        }
    }
};
