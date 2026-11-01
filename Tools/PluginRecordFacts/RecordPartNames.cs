namespace AddedObjectRemover;

/// <summary>The parts of a placed record, each compared and written as one unit.</summary>
[Flags]
public enum PlacedParts
{
    None = 0,
    Position = 1 << 0,
    Rotation = 1 << 1,
    Scale = 1 << 2,

    /// <summary>The Initially Disabled and Deleted flags with the enable parent.</summary>
    Presence = 1 << 3,

    /// <summary>A door's link to a navmesh triangle (XNDP).</summary>
    NavDoorLink = 1 << 4,

    /// <summary>The base object with every field no other part holds.</summary>
    BaseAndData = 1 << 5,
}

/// <summary>The parts of a cell record, each compared and written as one unit.</summary>
public enum CellPart
{
    /// <summary>The lighting with its lighting template.</summary>
    Lighting,
    ImageSpace,
    AcousticSpace,
    Music,

    /// <summary>The Has Water flag with the water height.</summary>
    WaterLevel,

    /// <summary>The water type, velocity, noise texture and environment map.</summary>
    WaterLook,

    /// <summary>One region of the cell's region list: listed or not.</summary>
    Region,
    SkyAndWeatherFromRegion,
    Name,

    /// <summary>The owner with the faction rank.</summary>
    Ownership,
    EncounterZone,
    Location,
    CantTravelFromHere,
    NoLodWater,
    PublicArea,
    HandChanged,
    ShowSky,
    UseSkyLighting,
    OffLimits,
    CantWait,

    /// <summary>Every field no other part holds.</summary>
    RestOfCell,
}

/// <summary>One part of a cell record.</summary>
/// <param name="Region">For <see cref="CellPart.Region"/>, the region listed or not; null for every other part.</param>
public readonly record struct CellPartName(CellPart Part, RecordKey? Region);
