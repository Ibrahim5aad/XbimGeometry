using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Configures the behavior of dynamic deflection calculations.
/// Dynamic deflection aims to optimize triangulation size 
/// for swept solids based on their size and slenderness.
/// </summary>
public class DynamicDeflectionSettings
{
    /// <summary>
    /// Gets or sets the baseline section width in millimeters.
    /// This value serves as a reference for scaling the number of facets.
    /// The target number of facets is calculated by multiplying <see cref="MinimumPerimeterFacets"/>
    /// by a size ratio: (actual minimum section dimension / BaselineSectionWidthMm).
    /// Thus, if an object's minimum section dimension (in mm) equals this baseline, the initial target facets will be <see cref="MinimumPerimeterFacets"/>.
    /// If the actual dimension is larger, target facets increase proportionally; if smaller, they decrease, before clamping and other adjustments.
    /// Default is 20.0 mm. And will take effect only if no  <see cref="CustomStrategy"/> is provided or if the custom strategy fails to provide a value.
    /// </summary>
    public double BaselineSectionWidthMm { get; set; } = 20.0;

    /// <summary>
    /// Gets or sets the minimum number of facets that a perimeter (e.g., of a swept profile) can have.
    /// This ensures that even small or slender objects maintain a basic level of geometric representation.
    /// Default is 3.
    /// </summary>
    public int MinimumPerimeterFacets { get; set; } = 3;

    /// <summary>
    /// Gets or sets the maximum number of facets that a perimeter can have.
    /// This acts as an upper limit to prevent excessively detailed tessellation,
    /// even for large objects or when custom strategies might suggest higher values.
    /// Default is 1000.
    /// </summary>
    public int MaximumPerimeterFacets { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the critical slenderness ratio (sweep length / smallest section dimension).
    /// Objects with a slenderness ratio below this value will not have their deflection dynamically adjusted
    /// and will use the default model deflection values.
    /// Default is 5.0.
    /// </summary>
    public double CriticalSlenderness { get; set; } = 5;

    /// <summary>
    /// Gets or sets the maximum linear deflection ratio relative to the effective radius of a section.
    /// This parameter controls how coarse the tessellation can be in terms of linear deviation.
    /// For example, a value of 1.5 means the linear deflection will not result in a deviation greater
    /// than 1.5 times the effective radius of the section from the true curve.
    /// Decrease to make the tessellation finer (less deflection allowed).
    /// Default is 1.5.
    /// </summary>
    public double MaxLinearDeflectionRatio { get; set; } = 1.5;

    /// <summary>
    /// Gets or sets the maximum angular deflection in radians.
    /// A smaller value leads to smoother curves (finer tessellation).
    /// Default is 1.5 * Math.PI.
    /// </summary>
    public double MaxAngularDeflectionRadians { get; set; } = 1.5 * Math.PI;

    /// <summary>
    /// Optional custom strategy for advanced control over target facet counts.
    /// If set, this strategy will be used to determine the number of facets based on section width and slenderness,
    /// potentially overriding the simple calculation based on <see cref="MinimumPerimeterFacets"/> and size ratio.
    /// Default is null (no custom strategy).
    /// </summary>
    public CustomDeflectionStrategy CustomStrategy { get; set; } = null;

    /// <summary>
    /// Creates a <see cref="DynamicDeflectionSettings"/> instance configured to achieve a target number of perimeter facets
    /// for objects matching the baseline section width.
    /// </summary>
    /// <param name="targetPerimeterFacets">The desired number of facets for a perimeter when its section width matches <paramref name="baselineSectionWidthMm"/>.</param>
    /// <param name="baselineSectionWidthMm">The reference section width in millimeters.</param>
    /// <param name="maximumPerimeterFacets">The absolute maximum number of facets a perimeter can have. Defaults to 1000.</param>
    /// <param name="criticalSlenderness">The slenderness ratio below which dynamic deflection is not applied. Defaults to 10.</param>
    /// <returns>A new <see cref="DynamicDeflectionSettings"/> instance.</returns>
    /// <exception cref="ArgumentException">Thrown if parameters are invalid (e.g., target facets &lt; 3, non-positive width).</exception>
    public static DynamicDeflectionSettings ForTargetFacetCount(
        int targetPerimeterFacets,
        double baselineSectionWidthMm,
        int maximumPerimeterFacets = 1000,
        double criticalSlenderness = 10)
    {
        if (targetPerimeterFacets < 3)
            throw new ArgumentException("Target perimeter facets must be at least 3", nameof(targetPerimeterFacets));

        if (baselineSectionWidthMm <= 0)
            throw new ArgumentException("Reference section width must be positive", nameof(baselineSectionWidthMm));

        if (maximumPerimeterFacets < targetPerimeterFacets)
            throw new ArgumentException("Maximum perimeter facets must be greater than or equal to target perimeter facets", nameof(maximumPerimeterFacets));

        if (criticalSlenderness <= 0)
            throw new ArgumentException("Critical slenderness must be positive", nameof(criticalSlenderness));

        return new DynamicDeflectionSettings
        {
            BaselineSectionWidthMm = baselineSectionWidthMm,
            MinimumPerimeterFacets = targetPerimeterFacets,
            CriticalSlenderness = criticalSlenderness,
            MaximumPerimeterFacets = maximumPerimeterFacets
        };
    }

    /// <summary>
    /// Creates a <see cref="DynamicDeflectionSettings"/> instance with a specified custom deflection strategy.
    /// Other settings will retain their default values.
    /// </summary>
    /// <param name="strategy">The custom deflection strategy to use.</param>
    /// <returns>A new <see cref="DynamicDeflectionSettings"/> instance with the custom strategy.</returns>
    public static DynamicDeflectionSettings WithCustomStrategy(CustomDeflectionStrategy strategy)
    {
        return new DynamicDeflectionSettings
        {
            CustomStrategy = strategy,
        };
    }
}


