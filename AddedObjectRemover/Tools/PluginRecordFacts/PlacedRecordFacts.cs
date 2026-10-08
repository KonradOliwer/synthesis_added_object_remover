using System.Collections.Immutable;
using System.Numerics;

namespace AddedObjectRemover;

/// <param name="Target">The plugin whose cells define the spaces to read; its own placed records can be written later.</param>
/// <param name="Terrain">Also find the land of the target worldspaces, for <see cref="IPluginRecords.ReadTerrain"/>.</param>
/// <param name="Navmeshes">Also find the navmeshes of the target spaces, for <see cref="IPluginRecords.ReadNavmeshes"/>.</param>
public sealed record PlacedReadScope(PluginName Target, bool Terrain, bool Navmeshes);

/// <summary>One winning placed record as its plugin stores it; it is not judged here.</summary>
/// <param name="WinningMod">The plugin whose version of the record wins.</param>
/// <param name="Cell">The cell whose child list holds the winning version; for an interior it is the space itself.</param>
/// <param name="InTargetSpace">The record's space holds a record created by the target plugin.</param>
/// <param name="Scale">As the record stores it; null when it has none.</param>
/// <param name="ReferenceRadius">The reference's own radius (XRDS) as the record stores it; null when it has none.</param>
/// <param name="PrimitiveBounds">The reference's primitive box, full sizes as the record exposes them; null when it is not a primitive.</param>
public sealed record PlacedRecordFact(
    RecordKey Key,
    string? EditorId,
    PluginName WinningMod,
    SpaceFact Space,
    CellFact Cell,
    bool InTargetSpace,
    PlacementFacts Placement,
    float? Scale,
    BaseKey? Base,
    bool IsTeleportDoor,
    bool IsPrimitive,
    bool HasMapMarker,
    float? ReferenceRadius,
    Vector3? PrimitiveBounds);

/// <summary>What reading the placed records of the load order found.</summary>
/// <param name="Records">
/// Each record once, by its first sighting (plugins from the highest priority down, in each cell the persistent children before
/// the temporary ones), without deleted ones. Only records in a target space, created by the target plugin, or overridden by it.
/// </param>
/// <param name="RecordsScanned">Every record seen once, deleted ones and those in other spaces included.</param>
/// <param name="OverriddenRecords">Placed records of other plugins that the target plugin overrides.</param>
/// <param name="Links">
/// Every link from a placed record into the target plugin, in the order found, read from the same walk as the records;
/// links from other records come from <see cref="IPluginRecords.ReadLinks"/>.
/// </param>
public sealed record PlacedRecordFacts(
    ImmutableArray<PlacedRecordFact> Records,
    int RecordsScanned,
    IReadOnlySet<RecordKey> OverriddenRecords,
    ImmutableArray<LinkFact> Links,
    int NavmeshCount);
