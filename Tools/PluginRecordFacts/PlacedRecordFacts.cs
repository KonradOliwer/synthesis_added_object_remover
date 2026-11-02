using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover;

/// <param name="Plugin">The plugin the read is made around: its cells define the spaces to read; the placed records of those spaces can be written later.</param>
/// <param name="Terrain">Also find the land of the plugin worldspaces, for <see cref="IPluginRecords.ReadTerrain"/>.</param>
/// <param name="Navmeshes">Also find the navmeshes of the plugin spaces, for <see cref="IPluginRecords.ReadNavmeshes"/>.</param>
/// <param name="TerrainVersions">Also keep every plugin's own land record of the plugin worldspaces' cells, for <see cref="IPluginRecords.ReadTerrainVersions"/>.</param>
/// <param name="TerrainLayers">Also decode the texture layers of those land records, for <see cref="TerrainVersion.Layers"/>; needs <paramref name="TerrainVersions"/>.</param>
/// <param name="Water">
/// Also keep every plugin's own water fields of the plugin worldspaces' own exterior cells and of the worldspaces that supply their
/// default water, for <see cref="IPluginRecords.ReadWaterCopies"/>.
/// </param>
/// <param name="HoldKeyword">The keyword a Location carries when it is a hold; the nearest such Location in a cell's Location chain names the cell's hold.</param>
/// <param name="TerrainReadsNeedAnOverride">
/// <paramref name="TerrainVersions"/>, <paramref name="TerrainLayers"/> and <paramref name="Water"/> are asked for only to check records the
/// read's plugin overrides: they are skipped when it overrides no placed record of another plugin.
/// </param>
/// <param name="PartTolerances">The gaps that are no change when the parts of a record the read's plugin overrides are compared (<see cref="PlacedRecordFact.PartDifferences"/>).</param>
/// <param name="AreaRecordVersions">
/// Also compare, part by part, the cell and LAND records of other plugins that the read's plugin overrides, as far as asked for, and
/// the navmeshes of the plugin spaces it overrides, deletes or adds (<see cref="PlacedRecordFacts.ChangedCells"/>,
/// <see cref="PlacedRecordFacts.ChangedLands"/>, <see cref="PlacedRecordFacts.ChangedNavmeshes"/>); null when not asked for.
/// </param>
public sealed record PlacedReadScope(
    PluginName Plugin,
    bool Terrain,
    bool Navmeshes,
    bool TerrainVersions,
    bool TerrainLayers,
    bool Water,
    RecordKey HoldKeyword,
    bool TerrainReadsNeedAnOverride,
    PlacedPartTolerances PartTolerances,
    AreaRecordReads? AreaRecordVersions);

/// <summary>The largest gap per value of a placed record that is no change: rounding by plugins that re-save a record.</summary>
/// <param name="Position">Per axis, in game units.</param>
/// <param name="Rotation">Per axis, in radians.</param>
public readonly record struct PlacedPartTolerances(float Position, float Rotation, float Scale)
{
    /// <summary>Plugins that re-save a record round its floats, so two positions or vertices this close on every axis, in game units, coincide.</summary>
    public const float ResavedPosition = 0.01f;
}

/// <summary>One placed record: its winning version as its plugin stores it, and the versions below it that were kept. It is not judged here.</summary>
/// <param name="Cell">The cell whose child list holds the winning version; for an interior it is the space itself.</param>
/// <param name="InPluginSpace">
/// The record lies in a plugin space: one where the read's plugin places a record, one it created or one it overrides.
/// </param>
/// <param name="IsPersistent">The record's own Persistent flag is set.</param>
/// <param name="NamedByScript">The first quest or scene that a script on the record itself names; null when none does.</param>
/// <param name="HasLocationRefType">The record itself has at least one location ref type (XLRT).</param>
/// <param name="ReferenceRadius">The reference's own radius (XRDS) as the record stores it; null when it has none.</param>
/// <param name="PrimitiveBounds">The reference's primitive box, full sizes as the record exposes them; null when it is not a primitive.</param>
/// <param name="Versions">
/// Lowest first, the winner last: the record's versions from the winner down to and including the first one whose plugin stops the
/// walk (<see cref="IPluginRecords.ReadPlacedRecords"/>), or all of them when none does. Each plugin gives one version.
/// </param>
/// <param name="PartDifferences">
/// Only for a record of another plugin that the read's plugin overrides and that has a version below the read's plugin's; null for
/// every other record. It sets every part of each version from that one up to the winner against the others.
/// </param>
public sealed record PlacedRecordFact(
    RecordKey Key,
    string? EditorId,
    SpaceFact Space,
    CellFact Cell,
    bool InPluginSpace,
    bool IsTeleportDoor,
    bool IsPrimitive,
    bool HasMapMarker,
    bool IsPersistent,
    ScriptNamedRecord? NamedByScript,
    bool HasLocationRefType,
    float? ReferenceRadius,
    Vector3? PrimitiveBounds,
    ImmutableArray<PlacedVersionFact> Versions,
    PlacedPartDifferences? PartDifferences)
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
/// the temporary ones); a deleted winning version is a fact too, so no lower version stands in for it. Only records in a plugin
/// space, created by the read's plugin, overridden by it, or linking into it.
/// </param>
/// <param name="RecordsScanned">Every record seen once, deleted ones and those in other spaces included.</param>
/// <param name="OverriddenRecords">Placed records of other plugins that the read's plugin overrides.</param>
/// <param name="Links">
/// Every link from a placed record into the read's plugin, and from a placed record of a plugin space into a record the read's
/// plugin overrides, in the order found, read from the same walk as the records; links from other records come from
/// <see cref="IPluginRecords.ReadLinks"/>.
/// </param>
/// <param name="CellAreas">The winning cell records of the plugin spaces, by cell key.</param>
/// <param name="LinkedFromPlacedRecords">Every record that the winning version of a placed record of a plugin space links to, other than its base.</param>
/// <param name="ChangedCells">
/// Only with <see cref="AreaRecordReads.Cells"/>: the cell records of other plugins in the plugin spaces that the read's
/// plugin changed, in the order their walk ended; the persistent cell of a worldspace is none of them.
/// </param>
/// <param name="ChangedLands">
/// Only with <see cref="AreaRecordReads.Lands"/>: the LAND records of other plugins that the read's plugin changed in the
/// worldspaces whose land a plugin space uses, in the order their walk ended.
/// </param>
/// <param name="ChangedNavmeshes">
/// Only with <see cref="PlacedReadScope.AreaRecordVersions"/>: the navmeshes of the plugin spaces that the read's plugin overrides,
/// deletes or adds, with a part it changed, in <see cref="RecordKeyOrder"/>.
/// </param>
/// <param name="DeletedCells">Only with <see cref="PlacedReadScope.AreaRecordVersions"/>: the cells of the plugin spaces that some plugin's version deletes.</param>
/// <param name="OverriddenDoorLinks">
/// Only with <see cref="PlacedReadScope.AreaRecordVersions"/>: the navmesh link of each placed record the read's plugin overrides
/// that has one in its version below the read's plugin's or its winning version, by key.
/// </param>
/// <param name="CellsDeletedInFinalWorld">The cells of the plugin spaces whose winning version deletes them, each with that version's plugin.</param>
/// <param name="Spaces">Every space whose cells the walk read, by key.</param>
public sealed record PlacedRecordFacts(
    ImmutableArray<PlacedRecordFact> Records,
    int RecordsScanned,
    IReadOnlySet<RecordKey> OverriddenRecords,
    ImmutableArray<LinkFact> Links,
    int NavmeshCount,
    IReadOnlyDictionary<RecordKey, CellAreaFact> CellAreas,
    IReadOnlySet<RecordKey> LinkedFromPlacedRecords,
    ImmutableArray<CellRecordFact> ChangedCells,
    ImmutableArray<LandRecordFact> ChangedLands,
    ImmutableArray<NavmeshRecordFact> ChangedNavmeshes,
    IReadOnlySet<RecordKey> DeletedCells,
    IReadOnlyDictionary<RecordKey, NavDoorLinkVersions> OverriddenDoorLinks,
    IReadOnlyDictionary<RecordKey, PluginName> CellsDeletedInFinalWorld,
    IReadOnlyDictionary<RecordKey, SpaceFact> Spaces);
