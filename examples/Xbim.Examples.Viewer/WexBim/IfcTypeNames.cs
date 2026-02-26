namespace Xbim.Examples.Viewer.WexBim;

/// <summary>
/// Maps IFC entity type short IDs to human-readable names.
/// Covers common IFC4 building element types encountered in WexBIM files.
/// Type IDs that are not in the dictionary are displayed as their numeric value.
/// </summary>
internal static class IfcTypeNames
{
    private static readonly Dictionary<short, string> Names = new()
    {
        // Spatial structure
        [17] = "IfcSite",
        [26] = "IfcBuilding",
        [46] = "IfcBuildingStorey",
        [56] = "IfcSpace",

        // Walls
        [39] = "IfcWall",
        [40] = "IfcWallStandardCase",
        [710] = "IfcWallElementedCase",

        // Slabs & floors
        [52] = "IfcSlab",
        [696] = "IfcSlabStandardCase",
        [697] = "IfcSlabElementedCase",

        // Roofs
        [50] = "IfcRoof",

        // Columns & beams
        [15] = "IfcColumn",
        [686] = "IfcColumnStandardCase",
        [6] = "IfcBeam",
        [679] = "IfcBeamStandardCase",

        // Openings & doors & windows
        [42] = "IfcOpeningElement",
        [20] = "IfcDoor",
        [689] = "IfcDoorStandardCase",
        [61] = "IfcWindow",
        [719] = "IfcWindowStandardCase",

        // Stairs & ramps
        [57] = "IfcStairFlight",
        [58] = "IfcStair",
        [49] = "IfcRampFlight",
        [48] = "IfcRamp",

        // Railings & coverings
        [47] = "IfcRailing",
        [16] = "IfcCovering",
        [18] = "IfcCurtainWall",

        // MEP elements
        [199] = "IfcFlowSegment",
        [202] = "IfcFlowTerminal",
        [193] = "IfcFlowFitting",
        [189] = "IfcFlowController",
        [195] = "IfcFlowMovingDevice",
        [197] = "IfcFlowStorageDevice",
        [200] = "IfcFlowTreatmentDevice",
        [185] = "IfcEnergyConversionDevice",

        // Distribution elements
        [171] = "IfcDistributionElement",
        [173] = "IfcDistributionFlowElement",
        [172] = "IfcDistributionControlElement",

        // Furnishing & furniture
        [35] = "IfcFurnishingElement",
        [36] = "IfcFurniture",

        // Plates, members, footings
        [44] = "IfcPlate",
        [694] = "IfcPlateStandardCase",
        [41] = "IfcMember",
        [692] = "IfcMemberStandardCase",
        [34] = "IfcFooting",

        // Piles & foundations
        [43] = "IfcPile",

        // Reinforcing
        [571] = "IfcReinforcingBar",
        [573] = "IfcReinforcingMesh",
        [661] = "IfcTendon",
        [662] = "IfcTendonAnchor",

        // Proxy & building element proxy
        [246] = "IfcBuildingElementProxy",

        // Misc building elements
        [10] = "IfcBuildingElement",

        // Annotations & grids
        [107] = "IfcAnnotation",

        // Transport elements
        [60] = "IfcTransportElement",

        // Discrete accessories
        [181] = "IfcDiscreteAccessory",
        [187] = "IfcFastener",
        [192] = "IfcMechanicalFastener",

        // Civil / geotechnical (IFC4x3)
        [979] = "IfcBorehole",
        [987] = "IfcGeomodel",
        [988] = "IfcGeoslice",
        [985] = "IfcEarthworksFill",
        [984] = "IfcEarthworksCut",
    };

    /// <summary>
    /// Gets the human-readable IFC type name for a given type ID.
    /// Returns the numeric ID as a string if the type is not in the dictionary.
    /// </summary>
    public static string GetName(short typeId)
    {
        return Names.TryGetValue(typeId, out var name)
            ? name
            : $"Type #{typeId}";
    }

    /// <summary>
    /// Returns true if the type ID has a known human-readable name.
    /// </summary>
    public static bool IsKnown(short typeId) => Names.ContainsKey(typeId);

    /// <summary>
    /// Gets a short display name (without the "Ifc" prefix) for a type ID.
    /// </summary>
    public static string GetShortName(short typeId)
    {
        if (Names.TryGetValue(typeId, out var name))
        {
            return name.StartsWith("Ifc", StringComparison.Ordinal)
                ? name.Substring(3)
                : name;
        }

        return $"Type #{typeId}";
    }
}
