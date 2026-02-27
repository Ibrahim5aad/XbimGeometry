using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Represents a control point for a custom deflection strategy.
/// Each point defines a target number of facets for a specific combination
/// of section width (in millimeters) and slenderness ratio.
/// </summary>
public class DeflectionControlPoint
{
    /// <summary>
    /// Gets or sets the section width in millimeters for this control point.
    /// </summary>
    public double SectionWidthMm { get; set; }

    /// <summary>
    /// Gets or sets the slenderness ratio (sweep length / smallest section dimension) for this control point.
    /// </summary>
    public double SlendernessRatio { get; set; }

    /// <summary>
    /// Gets or sets the target number of facets to be generated when geometry matches
    /// the <see cref="SectionWidthMm"/> and <see cref="SlendernessRatio"/> of this control point.
    /// </summary>
    public int TargetFacets { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DeflectionControlPoint"/> class.
    /// </summary>
    /// <param name="sectionWidthMm">The section width in millimeters.</param>
    /// <param name="slendernessRatio">The slenderness ratio.</param>
    /// <param name="targetFacets">The target number of facets.</param>
    public DeflectionControlPoint(double sectionWidthMm, double slendernessRatio, int targetFacets)
    {
        SectionWidthMm = sectionWidthMm;
        SlendernessRatio = slendernessRatio;
        TargetFacets = targetFacets;
    }

    /// <summary>
    /// Returns a string representation of the control point.
    /// </summary>
    /// <returns>A string detailing the section width, slenderness, and target facets.</returns>
    public override string ToString()
    {
        return $"SectionWidth: {SectionWidthMm}mm, Slenderness: {SlendernessRatio}, TargetFacets: {TargetFacets}";
    }
}

/// <summary>
/// Defines the type of strategy to use for interpolating or selecting target facets
/// from the provided <see cref="CustomDeflectionStrategy.ControlPoints"/>.
/// </summary>
public enum StrategyType
{
    /// <summary>
    /// Uses bilinear interpolation between control points to determine the target facet count.
    /// If interpolation is not possible (e.g., insufficient points), it falls back to <see cref="NearestNeighbor"/>.
    /// </summary>
    BilinearInterpolation,

    /// <summary>
    /// Selects the target facet count from the control point that is closest (in terms of Euclidean distance
    /// in the SectionWidthMm-SlendernessRatio space) to the input geometry's characteristics.
    /// </summary>
    NearestNeighbor,
}

/// <summary>
/// Provides a customizable strategy for determining the target number of facets
/// for swept geometry based on its section width and slenderness.
/// This allows for fine-grained control over the level of detail, overriding default calculations.
/// </summary>
public class CustomDeflectionStrategy
{
    private const double NumericalTolerance = 1e-10;
    private readonly StrategyType _strategyType;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomDeflectionStrategy"/> class.
    /// </summary>
    /// <param name="strategyType">The interpolation/selection strategy to use. Defaults to <see cref="StrategyType.BilinearInterpolation"/>.</param>
    public CustomDeflectionStrategy(StrategyType strategyType = StrategyType.BilinearInterpolation)
    {
        _strategyType = strategyType;
    }

    /// <summary>
    /// Gets or sets the list of <see cref="DeflectionControlPoint"/> instances
    /// that define this custom strategy. These points are used by the chosen
    /// <see cref="StrategyType"/> to determine the target facet count.
    /// </summary>
    public List<DeflectionControlPoint> ControlPoints { get; set; } = new();

    /// <summary>
    /// Calculates the target number of facets for a given section width and slenderness ratio
    /// based on the <see cref="ControlPoints"/> and the specified <see cref="StrategyType"/>.
    /// </summary>
    /// <param name="sectionWidthMm">The section width of the geometry in millimeters.</param>
    /// <param name="slendernessRatio">The slenderness ratio of the geometry.</param>
    /// <returns>
    /// The calculated target number of facets. Returns a default of 6 if no control points are defined.
    /// Returns a minimum of 3 facets even if calculations result in a lower number.
    /// If <see cref="StrategyType.BilinearInterpolation"/> is used and fails (e.g. due to insufficient points),
    /// it falls back to a nearest neighbor approach.
    /// </returns>
    public int GetTargetFacets(double sectionWidthMm, double slendernessRatio)
    {
        if (!ControlPoints.Any())
            return 6;

        if (_strategyType == StrategyType.BilinearInterpolation)
        {
            var interpolated = BilinearInterpolate(sectionWidthMm, slendernessRatio);
            if (interpolated.HasValue)
                return Math.Max(3, (int)Math.Round(interpolated.Value)); // Minimum 3 facets
        }

        // If NearestNeighbor or bilinear interpolation fails
        var closest = ControlPoints
            .OrderBy(cp => Math.Pow(cp.SectionWidthMm - sectionWidthMm, 2) +
                          Math.Pow(cp.SlendernessRatio - slendernessRatio, 2))
            .FirstOrDefault();

        return closest?.TargetFacets ?? 6;
    }

    /// <summary>
    /// Performs bilinear interpolation using the control points to find a target facet value.
    /// </summary>
    private double? BilinearInterpolate(double x, double y)
    {
        var xValues = ControlPoints.Select(cp => cp.SectionWidthMm).Distinct().OrderBy(v => v).ToArray();
        var yValues = ControlPoints.Select(cp => cp.SlendernessRatio).Distinct().OrderBy(v => v).ToArray();

        if (xValues.Length < 2 || yValues.Length < 2)
            return null;

        var x1 = xValues.LastOrDefault(xv => xv <= x);
        var x2 = xValues.FirstOrDefault(xv => xv >= x);

        var y1 = yValues.LastOrDefault(yv => yv <= y);
        var y2 = yValues.FirstOrDefault(yv => yv >= y);

        if (x1 == 0 && x2 == 0) // x is below all values
        {
            x1 = xValues[0];
            x2 = xValues.Length > 1 ? xValues[1] : x1;
        }
        else if (x1 == x2) // x is above all values or exactly on a point
        {
            var index = Array.IndexOf(xValues, x1);
            if (index > 0)
            {
                x1 = xValues[index - 1];
            }
            else if (index < xValues.Length - 1)
            {
                x2 = xValues[index + 1];
            }
        }

        if (y1 == 0 && y2 == 0) // y is below all values
        {
            y1 = yValues[0];
            y2 = yValues.Length > 1 ? yValues[1] : y1;
        }
        else if (y1 == y2) // y is above all values or exactly on a point
        {
            var index = Array.IndexOf(yValues, y1);
            if (index > 0)
            {
                y1 = yValues[index - 1];
            }
            else if (index < yValues.Length - 1)
            {
                y2 = yValues[index + 1];
            }
        }

        var f11 = GetValueAt(x1, y1);
        var f12 = GetValueAt(x1, y2);
        var f21 = GetValueAt(x2, y1);
        var f22 = GetValueAt(x2, y2);

        if (!f11.HasValue || !f12.HasValue || !f21.HasValue || !f22.HasValue)
            return null;

        if (Math.Abs(x2 - x1) < NumericalTolerance && Math.Abs(y2 - y1) < NumericalTolerance)
            return f11.Value;

        if (Math.Abs(x2 - x1) < NumericalTolerance)
            return LinearInterpolate(y, y1, y2, f11.Value, f12.Value); // Vertical line

        if (Math.Abs(y2 - y1) < NumericalTolerance)
            return LinearInterpolate(x, x1, x2, f11.Value, f21.Value); // Horizontal line

        var denom = (x2 - x1) * (y2 - y1);
        return (f11.Value * (x2 - x) * (y2 - y) +
                f21.Value * (x - x1) * (y2 - y) +
                f12.Value * (x2 - x) * (y - y1) +
                f22.Value * (x - x1) * (y - y1)) / denom;
    }

    /// <summary>
    /// Retrieves the exact target facet value from a control point matching the given x (section width) and y (slenderness ratio).
    /// </summary>
    private int? GetValueAt(double x, double y)
    {
        const double tolerance = 1e-6;
        return ControlPoints
            .FirstOrDefault(cp => Math.Abs(cp.SectionWidthMm - x) < tolerance &&
                                 Math.Abs(cp.SlendernessRatio - y) < tolerance)
            ?.TargetFacets;
    }

    /// <summary>
    /// Performs linear interpolation between two points.
    /// </summary>
    private double LinearInterpolate(double x, double x1, double x2, double y1, double y2)
    {
        if (Math.Abs(x2 - x1) < NumericalTolerance)
            return y1;

        return y1 + (y2 - y1) * (x - x1) / (x2 - x1);
    }

    /// <summary>
    /// Adds a new control point to the strategy.
    /// </summary>
    public CustomDeflectionStrategy AddPoint(double sectionWidth, double slenderness, int facets)
    {
        ControlPoints.Add(new DeflectionControlPoint(sectionWidth, slenderness, facets));
        return this;
    }
}