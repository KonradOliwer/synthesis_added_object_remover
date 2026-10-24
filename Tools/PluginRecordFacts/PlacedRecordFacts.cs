using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover;

/// <param name="Target">The plugin whose cells define the spaces to read; its own placed records can be written later.</param>
/// <param name="Terrain">Also find the land of the target worldspaces, for <see cref="IPluginRecords.ReadTerrain"/>.</param>
/// <param name="Navmeshes">Also find the navmeshes of the target spaces, for <see cref="IPluginRecords.ReadNavmeshes"/>.</param>
/// <param name="TerrainVersions">Also keep every plugin's own land record of the target worldspaces' cells, for <see cref="IPluginRecords.ReadTerrainVersions"/>.</param>
/// <param name="TerrainLayers">Also decode the texture layers of those land records, for <see cref="TerrainVersion.Layers"/>; needs <paramref name="TerrainVersions"/>.</param>
/// <param name="Water">
/// Also keep every plugin's own water fields of the target worldspaces' own exterior cells and of the worldspaces that supply their
/// default water, for <see cref="IPluginRecords.ReadWaterCopies"/>.
/// </param>
/// <param name="HoldKeyword">The keyword a Location carries when it is a hold; the nearest such Location in a cell's Location chain names the cell's hold.</param>
public sealed record PlacedReadScope(PluginName Target, bool Terrain, bool Navmeshes, bool TerrainVersions, bool TerrainLayers, bool Water, RecordKey HoldKeyword);

/// <summary>One placed record: its winning version as its plugin stores it, and the versions below it that were kept. It is not judged here.</summary>
/// <param name="Cell">The cell whose child list holds the winning version; for an interior it is the space itself.</param>
/// <param name="InTargetSpace">The target plugin places a record in the record's space: one it created or one it overrides.</param>
/// <param name="IsPersistent">The record's own Persistent flag is set.</param>
/// <param name="NamedByScript">The first quest or scene that a script on the record itself names; null when none does.</param>
/// <param name="HasLocationRefType">The record itself has at least one location ref type (XLRT).</param>
/// <param name="ReferenceRadius">The reference's own radius (XRDS) as the record stores it; null when it has none.</param>
/// <param name="PrimitiveBounds">The reference's primitive box, full sizes as the record exposes them; null when it is not a primitive.</param>
/// <param name="Versions">
/// Lowest first, the winner last: the record's versions from the winner down to and including the first one whose plugin stops the
/// walk (<see cref="IPluginRecords.ReadPlacedRecords"/>), or all of them when none does. Each plugin gives one version.
/// </param>
public sealed record PlacedRecordFact(
    RecordKey Key,
    string? EditorId,
    SpaceFact Space,
    CellFact Cell,
    bool InTargetSpace,
    bool IsTeleportDoor,
    bool IsPrimitive,
    bool HasMapMarker,
    bool IsPersistent,
    ScriptNamedRecord? NamedByScript,
    bool HasLocationRefType,
    float? ReferenceRadius,
    Vector3? PrimitiveBounds,
    ImmutableArray<PlacedVersionFact> Versions)
{
    /// <summary>The winning version, the last of <see cref="Versions"/>.</summary>
    public PlacedVersionFact Winner => Versions[^1];

    /// <summary>The plugin whose version of the record wins.</summary>
    public PluginName WinningMod => Winner.Plugin;

    public PlacementFacts Placement => Winner.Placement;

    /// <summary>As the winning version stores it; null when it has none.</summary>
    public float? Scale => Winner.Scale;

    public BaseKey? Base => Winner.Base;
}

/// <summary>What reading the placed records of the load order found.</summary>
/// <param name="Records">
/// Each record once, by its first sighting (plugins from the highest priority down, in each cell the persistent children before
/// the temporary ones); a deleted winning version is a fact too, so no lower version stands in for it. Only records in a target
/// space, created by the target plugin, overridden by it, or linking into it.
/// </param>
/// <param name="RecordsScanned">Every record seen once, deleted ones and those in other spaces included.</param>
/// <param name="OverriddenRecords">Placed records of other plugins that the target plugin overrides.</param>
/// <param name="Links">
/// Every link from a placed record into the target plugin, and from a placed record of a target space into a record the target
/// plugin overrides, in the order found, read from the same walk as the records; links from other records come from
/// <see cref="IPluginRecords.ReadLinks"/>.
/// </param>
/// <param name="CellAreas">The winning cell records of the target spaces, by cell key.</param>
public sealed record PlacedRecordFacts(
    ImmutableArray<PlacedRecordFact> Records,
    int RecordsScanned,
    IReadOnlySet<RecordKey> OverriddenRecords,
    ImmutableArray<LinkFact> Links,
    int NavmeshCount,
    IReadOnlyDictionary<RecordKey, CellAreaFact> CellAreas);
