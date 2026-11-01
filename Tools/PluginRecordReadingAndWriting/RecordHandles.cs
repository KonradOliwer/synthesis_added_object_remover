using System.Collections.Immutable;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>A load-order record and where its override is written.</summary>
internal sealed record PlacedRecordHandle(IPlacedGetter Record, PlacedRecordLocation Location);

/// <summary>
/// The cell and LAND records of other plugins the target plugin changed, each with its versions from the one just below the target
/// plugin's up to the winner, the navmeshes it changed and every plugin's navmesh info map, and the part tables they were judged
/// with, so a part is written exactly as it was compared.
/// </summary>
internal sealed record AreaRecordHandles(
    IReadOnlyDictionary<RecordKey, RecordVersions<ICellGetter>> Cells,
    IReadOnlyDictionary<RecordKey, RecordVersions<ILandscapeGetter>> Lands,
    NavmeshRecordHandles Navmeshes,
    CellRecordParts CellParts,
    LandRecordParts LandParts);

/// <summary>
/// The plugin tool's record map: the winning load-order records of the target spaces, which can be written, by <see cref="RecordKey"/>.
/// Only the plugin tool reads it.
/// </summary>
/// <param name="parts">The part table the reader judged the records' parts with, so a part is written exactly as it was compared.</param>
/// <param name="areaRecords">Null when the area record versions were not asked for.</param>
internal sealed class RecordHandles(
    IReadOnlyDictionary<RecordKey, PlacedRecordHandle> handles,
    IReadOnlyDictionary<RecordKey, IPlacedGetter> earlierRecords,
    PlacedRecordParts parts,
    AreaRecordHandles? areaRecords)
    : IWritableRecords
{
    private const string AreaRecordsNotReadMessage = "The area record versions were not read; ask for them in the read scope.";

    public PlacedRecordParts Parts => parts;

    public IPlacedGetter RecordOf(RecordKey key) => handles[key].Record;

    /// <returns>Null when the record is no winning placed record of the target spaces.</returns>
    public IPlacedGetter? FindRecord(RecordKey key) => handles.TryGetValue(key, out var handle) ? handle.Record : null;

    /// <summary>The version of the record just below the target plugin's, for a record the target plugin overrides. A writer copies it; it never changes it.</summary>
    public IPlacedGetter EarlierRecordOf(RecordKey key) => earlierRecords[key];

    public PlacedRecordLocation LocationOf(RecordKey key) => handles[key].Location;

    /// <summary>A cell record of another plugin the target plugin overrides. A writer copies its versions; it never changes them.</summary>
    public RecordVersions<ICellGetter> CellOf(RecordKey key) => AreaRecords.Cells[key];

    /// <summary>A LAND record of another plugin the target plugin overrides. A writer copies its versions; it never changes them.</summary>
    public RecordVersions<ILandscapeGetter> LandOf(RecordKey key) => AreaRecords.Lands[key];

    /// <summary>A navmesh the target plugin overrides, deletes or adds. A writer copies its versions; it never changes them.</summary>
    public RecordVersions<NavmeshVersion?> NavmeshOf(RecordKey key) => AreaRecords.Navmeshes.Navmeshes[key];

    /// <summary>Every plugin's navmesh info map, in load order. A writer copies them; it never changes them.</summary>
    public ImmutableArray<NavmeshMapVersion> NavmeshMaps => AreaRecords.Navmeshes.Maps;

    /// <summary>The navmeshes read, the navmesh info maps and the door links of the target spaces. A reader never changes them.</summary>
    public NavmeshRecordHandles NavmeshRecords => AreaRecords.Navmeshes;

    public CellRecordParts CellParts => AreaRecords.CellParts;

    public LandRecordParts LandParts => AreaRecords.LandParts;

    private AreaRecordHandles AreaRecords => areaRecords ?? throw new InvalidOperationException(AreaRecordsNotReadMessage);
}
