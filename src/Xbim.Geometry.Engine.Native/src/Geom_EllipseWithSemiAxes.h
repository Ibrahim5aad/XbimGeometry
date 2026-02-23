/*
 * Geom_EllipseWithSemiAxes.h
 *
 * Extends Geom_Ellipse to handle IFC semi-axis semantics correctly.
 *
 * IFC defines SemiAxis1 along the local X-axis and SemiAxis2 along Y,
 * with no constraint on which is larger. OCCT requires majorRadius >= minorRadius
 * and always places the major axis along local X.
 *
 * When SemiAxis1 < SemiAxis2, this class swaps the radii AND rotates the
 * placement by -PI/2 so the OCCT major axis aligns with IFC's SemiAxis2
 * direction. It also adjusts Reverse() and provides ConvertIfcTrimParameter()
 * for correct trimmed-curve parameterization.
 */

#pragma once

#include <Geom_Ellipse.hxx>
#include <Standard_Type.hxx>
#include <Standard_DefineHandle.hxx>
#include <gp_Ax1.hxx>
#include <gp_Ax2.hxx>

#ifndef M_PI_2
#define M_PI_2 1.57079632679489661923
#endif

class Geom_EllipseWithSemiAxes;
DEFINE_STANDARD_HANDLE(Geom_EllipseWithSemiAxes, Geom_Ellipse)

class Geom_EllipseWithSemiAxes : public Geom_Ellipse
{
public:
    Geom_EllipseWithSemiAxes(const gp_Ax2& ax2, double semi1, double semi2)
        : Geom_Ellipse(ax2, Max(semi1, semi2), Min(semi1, semi2))
        , _rotated(false)
    {
        if (semi1 <= 0 || semi2 <= 0)
            throw Standard_ConstructionError("Geom_EllipseWithSemiAxes: semi-axes must be positive");

        if (semi1 < semi2)
        {
            gp_Ax1 zAx = ax2.Axis();
            gp_Ax2 axRot = ax2.Rotated(zAx, -M_PI_2);
            SetPosition(axRot);
            _rotated = true;
        }
    }

    Standard_Boolean IsRotated() const { return _rotated; }

    Standard_Real ConvertIfcTrimParameter(Standard_Real ifcTrimValue) const
    {
        return _rotated ? ifcTrimValue + M_PI_2 : ifcTrimValue;
    }

    void Reverse() override
    {
        if (_rotated)
        {
            gp_Ax1 zAx = pos.Axis();
            pos.Rotate(zAx, M_PI_2);
            gp_Dir vZ = pos.Direction();
            vZ.Reverse();
            pos.SetDirection(vZ);
            pos.Rotate(zAx, M_PI_2);
        }
        else
        {
            gp_Dir vZ = pos.Direction();
            vZ.Reverse();
            pos.SetDirection(vZ);
        }
    }

    Handle(Geom_Geometry) Copy() const override
    {
        return new Geom_EllipseWithSemiAxes(
            Position(), MajorRadius(), MinorRadius(), _rotated);
    }

private:
    Geom_EllipseWithSemiAxes(const gp_Ax2& ax2, double major, double minor, bool rotated)
        : Geom_Ellipse(ax2, major, minor)
        , _rotated(rotated)
    {}

    bool _rotated;
};
