/*
 * Geom_SegmentedReferenceCurve.h
 *
 * 3D curve that combines a gradient curve with superelevation (cant) segments.
 * Evaluates as:
 *   P(u) = baseCurve.D0(u) + (0, 0, superElevation(u))
 *
 * Each superelevation segment is a 2D curve paired with a TopLoc_Location that
 * encodes the segment's starting superelevation (Y translation) and cant tilt
 * (rotation around X). Used for road/railway alignment geometry with banking
 * (IfcSegmentedReferenceCurve).
 */

#pragma once

#include "Geom_ConvertibleToBSpline.h"
#include "Geom_GradientCurve.h"
#include "Geom2d_Spiral.h"
#include "Geom2d_PolynomialSpiral.h"
#include "Geom2d_Polynomial.h"

#include <Geom2d_Curve.hxx>
#include <Geom2dAdaptor_Curve.hxx>
#include <GCPnts_AbscissaPoint.hxx>
#include <GCPnts_UniformAbscissa.hxx>
#include <GeomAPI_PointsToBSpline.hxx>
#include <TColgp_Array1OfPnt.hxx>
#include <TopLoc_Location.hxx>
#include <Precision.hxx>
#include <Standard_NotImplemented.hxx>

#include <vector>
#include <tuple>
#include <cmath>
#include <algorithm>

class Geom_SegmentedReferenceCurve;
DEFINE_STANDARD_HANDLE(Geom_SegmentedReferenceCurve, Geom_ConvertibleToBSpline)

class Geom_SegmentedReferenceCurve : public Geom_ConvertibleToBSpline
{
public:
    Geom_SegmentedReferenceCurve(
        const Handle(Geom_GradientCurve)& baseCurve,
        const std::vector<std::pair<Handle(Geom2d_Curve), TopLoc_Location>>& superElevationFunction,
        const TopLoc_Location& endPoint,
        bool hasEndPoint)
        : _baseCurve(baseCurve),
          _superElevationFunction(superElevationFunction),
          _endPoint(endPoint),
          _hasEndPoint(hasEndPoint),
          _totalLength(0.0)
    {
        _firstParam = _baseCurve->FirstParameter();
        _lastParam = _baseCurve->LastParameter();

        for (const auto& segmentPair : _superElevationFunction)
        {
            Geom2dAdaptor_Curve adaptor(segmentPair.first);
            double segLen = GCPnts_AbscissaPoint::Length(
                adaptor, adaptor.FirstParameter(), adaptor.LastParameter());
            _totalLength += segLen;
            _cumulativeSpans.push_back(_totalLength);
        }
    }

    /* ---- Evaluation ---- */

    void D0(Standard_Real U, gp_Pnt& P) const override
    {
        U = Clamp(U);
        gp_Pnt basePnt;
        _baseCurve->D0(U, basePnt);

        auto [superElev, cantTilt] = GetSuperelevationAndCantTiltAt(U);
        P.SetCoord(basePnt.X(), basePnt.Y(), basePnt.Z() + superElev);
    }

    void D1(Standard_Real U, gp_Pnt& P, gp_Vec& V1) const override
    {
        U = Clamp(U);
        gp_Pnt basePnt;
        gp_Vec baseV1;
        _baseCurve->D1(U, basePnt, baseV1);

        auto [superElev, cantTilt] = GetSuperelevationAndCantTiltAt(U);
        P.SetCoord(basePnt.X(), basePnt.Y(), basePnt.Z() + superElev);
        V1 = baseV1;
    }

    void D2(Standard_Real U, gp_Pnt& P, gp_Vec& V1, gp_Vec& V2) const override
    {
        U = Clamp(U);
        gp_Pnt basePnt;
        gp_Vec baseV1, baseV2;
        _baseCurve->D2(U, basePnt, baseV1, baseV2);

        auto [superElev, cantTilt] = GetSuperelevationAndCantTiltAt(U);
        P.SetCoord(basePnt.X(), basePnt.Y(), basePnt.Z() + superElev);
        V1 = baseV1;
        V2 = baseV2;
    }

    void D3(Standard_Real U, gp_Pnt& P,
            gp_Vec& V1, gp_Vec& V2, gp_Vec& V3) const override
    {
        U = Clamp(U);
        gp_Pnt basePnt;
        gp_Vec baseV1, baseV2, baseV3;
        _baseCurve->D3(U, basePnt, baseV1, baseV2, baseV3);

        auto [superElev, cantTilt] = GetSuperelevationAndCantTiltAt(U);
        P.SetCoord(basePnt.X(), basePnt.Y(), basePnt.Z() + superElev);
        V1 = baseV1;
        V2 = baseV2;
        V3 = baseV3;
    }

    gp_Vec DN(Standard_Real U, Standard_Integer N) const override
    {
        if (N == 1) { gp_Pnt P; gp_Vec V1; D1(U, P, V1); return V1; }
        if (N == 2) { gp_Pnt P; gp_Vec V1, V2; D2(U, P, V1, V2); return V2; }
        if (N == 3) { gp_Pnt P; gp_Vec V1, V2, V3; D3(U, P, V1, V2, V3); return V3; }
        throw Standard_NotImplemented(
            "Geom_SegmentedReferenceCurve::DN not implemented for N > 3");
    }

    /* ---- Curve properties ---- */

    Standard_Real FirstParameter() const override { return _firstParam; }
    Standard_Real LastParameter()  const override { return _lastParam; }

    GeomAbs_Shape Continuity() const override { return _baseCurve->Continuity(); }

    Standard_Boolean IsClosed() const override
    {
        gp_Pnt ps, pe;
        D0(FirstParameter(), ps);
        D0(LastParameter(), pe);
        return ps.IsEqual(pe, Precision::Confusion());
    }

    Standard_Boolean IsPeriodic() const override { return _baseCurve->IsPeriodic(); }
    Standard_Boolean IsCN(Standard_Integer N) const override { return _baseCurve->IsCN(N); }

    void Reverse() override
    {
        throw Standard_NotImplemented(
            "Geom_SegmentedReferenceCurve::Reverse not implemented");
    }

    Standard_Real ReversedParameter(Standard_Real U) const override
    {
        return _firstParam + _lastParam - U;
    }

    void Transform(const gp_Trsf& /*T*/) override
    {
        throw Standard_NotImplemented(
            "Geom_SegmentedReferenceCurve::Transform not implemented");
    }

    Handle(Geom_Geometry) Copy() const override { return Clone(); }

    Handle(Geom_SegmentedReferenceCurve) Clone() const
    {
        auto baseCopy = Handle(Geom_GradientCurve)::DownCast(_baseCurve->Copy());

        std::vector<std::pair<Handle(Geom2d_Curve), TopLoc_Location>> segsCopy;
        segsCopy.reserve(_superElevationFunction.size());
        for (const auto& seg : _superElevationFunction)
        {
            auto curveCopy = Handle(Geom2d_Curve)::DownCast(seg.first->Copy());
            segsCopy.emplace_back(curveCopy, seg.second);
        }

        auto copy = new Geom_SegmentedReferenceCurve(
            baseCopy, segsCopy, _endPoint, _hasEndPoint);
        copy->_cumulativeSpans = _cumulativeSpans;
        copy->_totalLength = _totalLength;
        return copy;
    }

    /* ---- ToBSpline ---- */

    Handle(Geom_BSplineCurve) ToBSpline(int nbPoints) const override
    {
        double p1 = _baseCurve->HorizontalCurve()->FirstParameter();
        double p2 = _baseCurve->HorizontalCurve()->LastParameter();
        return ToBSpline(p1, p2, nbPoints);
    }

    Handle(Geom_BSplineCurve) ToBSpline(
        double startParam, double endParam, int nbPoints) const override
    {
        int totalPoints = nbPoints + (_hasEndPoint ? 1 : 0);
        TColgp_Array1OfPnt points(1, totalPoints);

        Geom2dAdaptor_Curve adaptor(
            _baseCurve->HorizontalCurve(), startParam, endParam);
        GCPnts_UniformAbscissa sampler(adaptor, nbPoints);

        for (Standard_Integer i = 1; i <= nbPoints; ++i)
        {
            Standard_Real param = sampler.Parameter(i);

            gp_Pnt2d hPt;
            _baseCurve->HorizontalCurve()->D0(param, hPt);

            auto [superElev, cantTilt] = GetSuperelevationAndCantTiltAt(param);

            gp_Pnt2d vPt;
            _baseCurve->HeightFunction()->D0(param, vPt);

            Standard_Real z = vPt.Y() + superElev;
            points.SetValue(i, gp_Pnt(hPt.X(), hPt.Y(), z));
        }

        GeomAPI_PointsToBSpline fitter(points, 8, 8, GeomAbs_CN);
        return fitter.Curve();
    }

    /* ---- Accessors ---- */

    const Handle(Geom_GradientCurve)& BaseCurve() const { return _baseCurve; }

    std::string DumpSegmentInfo() const
    {
        std::ostringstream os;
        os << "SegmentedReferenceCurve segment dump:\n";
        os << "  totalLength: " << _totalLength << "\n";
        os << "  numSegments: " << _superElevationFunction.size() << "\n";
        os << "  cumulativeSpans: [";
        for (size_t i = 0; i < _cumulativeSpans.size(); ++i)
            os << (i ? ", " : "") << _cumulativeSpans[i];
        os << "]\n\n";

        for (size_t i = 0; i < _superElevationFunction.size(); ++i)
        {
            const auto& loc = _superElevationFunction[i].second;
            const auto& curve2d = _superElevationFunction[i].first;
            auto trans = loc.Transformation().TranslationPart();
            auto tilt = GetRotationAroundX(loc);

            os << "  Segment " << i << ":\n";
            os << "    Location Y (superElev): " << trans.Y() << "\n";
            os << "    Location X: " << trans.X() << ", Z: " << trans.Z() << "\n";
            os << "    Tilt (rotation around X): " << tilt << "\n";
            os << "    cumulativeSpan: " << (i < _cumulativeSpans.size() ? _cumulativeSpans[i] : -1) << "\n";

            if (!curve2d.IsNull())
            {
                os << "    2D curve type: " << curve2d->DynamicType()->Name() << "\n";
                os << "    2D firstParam: " << curve2d->FirstParameter() << "\n";
                os << "    2D lastParam: " << curve2d->LastParameter() << "\n";
                auto startPt2d = curve2d->Value(curve2d->FirstParameter());
                os << "    2D startPt: (" << startPt2d.X() << ", " << startPt2d.Y() << ")\n";
                auto endPt = curve2d->Value(curve2d->LastParameter());
                os << "    2D endPt: (" << endPt.X() << ", " << endPt.Y() << ")\n";

                // Test DownCasts
                auto asSpiral = Handle(Geom2d_Spiral)::DownCast(curve2d);
                auto asPoly = Handle(Geom2d_PolynomialSpiral)::DownCast(curve2d);
                os << "    DownCast Spiral: " << (!asSpiral.IsNull() ? "YES" : "NO") << "\n";
                os << "    DownCast PolynomialSpiral: " << (!asPoly.IsNull() ? "YES" : "NO") << "\n";

                if (!asSpiral.IsNull())
                {
                    auto plcLoc = asSpiral->Placement().Location();
                    os << "    Spiral Placement Origin: (" << plcLoc.X() << ", " << plcLoc.Y() << ")\n";
                    os << "    Spiral curvature@first: " << asSpiral->GetCurvatureAt(asSpiral->FirstParameter()) << "\n";
                    os << "    Spiral curvature@last: " << asSpiral->GetCurvatureAt(asSpiral->LastParameter()) << "\n";
                }
            }
            os << "\n";
        }
        return os.str();
    }

    /*
     * Compute superelevation and cant tilt angle at the given distance-along
     * parameter. Interpolates between superelevation segments using the
     * curvature gradient of each segment's 2D curve.
     */
    std::tuple<Standard_Real, Standard_Real>
    GetSuperelevationAndCantTiltAt(Standard_Real x_value) const
    {
        if (_superElevationFunction.empty())
            return std::make_tuple(0.0, 0.0);

        if (x_value <= 0.0)
        {
            auto startY = _superElevationFunction.front().second
                              .Transformation().TranslationPart().Y();
            return std::make_tuple(startY, 0.0);
        }

        if (x_value >= _totalLength)
        {
            auto endY = _superElevationFunction.back().second
                            .Transformation().TranslationPart().Y();
            return std::make_tuple(endY, 0.0);
        }

        /* Find the active segment */
        size_t segIdx = 0;
        for (; segIdx < _cumulativeSpans.size(); ++segIdx)
        {
            if (x_value <= _cumulativeSpans[segIdx])
                break;
        }

        const auto& currentLoc = _superElevationFunction[segIdx].second;
        const auto& rateOfChange = _superElevationFunction[segIdx].first;

        Handle(Geom2d_Spiral) spiral =
            Handle(Geom2d_Spiral)::DownCast(rateOfChange);
        Handle(Geom2d_Polynomial) polynomial =
            Handle(Geom2d_Polynomial)::DownCast(rateOfChange);

        Standard_Real startTilt = GetRotationAroundX(currentLoc);
        Standard_Real startSuperElev =
            currentLoc.Transformation().TranslationPart().Y();

        /* Local parameter within this segment */
        double prevSpan = (segIdx > 0) ? _cumulativeSpans[segIdx - 1] : 0.0;
        double localParam = x_value - prevSpan;

        Standard_Real delta1 = 0.0, delta2 = 0.0, deltaCurrent = 0.0;

        if (!spiral.IsNull())
        {
            delta1 = spiral->GetCurvatureAt(spiral->FirstParameter());
            delta2 = spiral->GetCurvatureAt(spiral->LastParameter());
            deltaCurrent = spiral->GetCurvatureAt(localParam);
        }
        else if (!polynomial.IsNull())
        {
            delta1 = polynomial->GetCurvatureAt(polynomial->FirstParameter());
            delta2 = polynomial->GetCurvatureAt(polynomial->LastParameter());
            deltaCurrent = polynomial->GetCurvatureAt(localParam);
        }

        const Standard_Real epsilon = 1e-9;

        if (segIdx < _superElevationFunction.size() - 1)
        {
            /* There is a next segment — interpolate toward it */
            const auto& nextLoc = _superElevationFunction[segIdx + 1].second;
            Standard_Real endTilt = GetRotationAroundX(nextLoc);
            Standard_Real endSuperElev =
                nextLoc.Transformation().TranslationPart().Y();

            Standard_Real denom = delta2 - delta1;

            if (spiral.IsNull() && polynomial.IsNull() ||
                std::fabs(denom) < epsilon)
            {
                return std::make_tuple(startSuperElev, startTilt);
            }

            Standard_Real t = (deltaCurrent - delta1) / denom;
            Standard_Real curTilt = (endTilt - startTilt) * t + startTilt;
            Standard_Real curSuperElev =
                (endSuperElev - startSuperElev) * t + startSuperElev;
            return std::make_tuple(curSuperElev, curTilt);
        }
        else
        {
            /* Last segment — interpolate toward zero */
            Standard_Real denom = delta2 - delta1;

            if (spiral.IsNull() && polynomial.IsNull() ||
                std::fabs(denom) < epsilon)
            {
                return std::make_tuple(startSuperElev, startTilt);
            }

            Standard_Real t = (deltaCurrent - delta1) / denom;
            Standard_Real curTilt = (0.0 - startTilt) * t + startTilt;
            Standard_Real curSuperElev =
                (0.0 - startSuperElev) * t + startSuperElev;
            return std::make_tuple(curSuperElev, curTilt);
        }
    }

private:
    Handle(Geom_GradientCurve) _baseCurve;
    std::vector<std::pair<Handle(Geom2d_Curve), TopLoc_Location>> _superElevationFunction;
    TopLoc_Location _endPoint;
    bool _hasEndPoint;
    Standard_Real _firstParam;
    Standard_Real _lastParam;
    std::vector<double> _cumulativeSpans;
    double _totalLength;

    Standard_Real Clamp(Standard_Real U) const
    {
        if (U < _firstParam) return _firstParam;
        if (U > _lastParam)  return _lastParam;
        return U;
    }

    /*
     * Extract the rotation angle around X from a TopLoc_Location transform.
     * Returns atan2(M32, M22) of the 3x3 rotation matrix.
     */
    static Standard_Real GetRotationAroundX(const TopLoc_Location& loc)
    {
        const gp_Trsf& trsf = loc.Transformation();
        return std::atan2(trsf.Value(3, 2), trsf.Value(2, 2));
    }
};
