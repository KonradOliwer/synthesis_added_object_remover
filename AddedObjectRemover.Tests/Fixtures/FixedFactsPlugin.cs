using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>A load order that holds only the given placed record facts and links; every other read throws.</summary>
/// <param name="overridden">The records of other plugins the target plugin overrides.</param>
internal sealed class FixedFactsPlugin(ImmutableArray<PlacedRecordFact> records, ImmutableArray<LinkFact> links, IReadOnlySet<RecordKey> overridden) : IPluginRecords
{
    public FixedFactsPlugin(ImmutableArray<PlacedRecordFact> records)
        : this(records, [], new HashSet<RecordKey>())
    {
    }

    public FixedFactsPlugin(ImmutableArray<PlacedRecordFact> records, ImmutableArray<LinkFact> links)
        : this(records, links, new HashSet<RecordKey>())
    {
    }

    /// <summary>The stop predicate of the last read; null before any.</summary>
    public Func<PluginName, bool>? StopsTheWalk { get; private set; }

    /// <summary>
    /// The parts the facts of a version show, each with its compare; the base part stands for the base and data. The facts hold no
    /// bytes Mutagen does not read, so every value can be written back.
    /// </summary>
    private static readonly PlacedPartCompare<PlacedVersionFact>[] VersionParts =
    [
        Part(PlacedParts.Position, static (one, other) => one.Placement.Position != other.Placement.Position),
        Part(PlacedParts.Rotation, static (one, other) => one.Placement.EulerRotation != other.Placement.EulerRotation),
        Part(PlacedParts.Scale, static (one, other) => one.Scale != other.Scale),
        Part(PlacedParts.Presence, static (one, other) => one.Placement.Presence != other.Placement.Presence),
        Part(PlacedParts.BaseAndData, static (one, other) => one.Base != other.Base || one.Reference != other.Reference),
    ];

    private static PlacedPartCompare<PlacedVersionFact> Part(PlacedParts part, Func<PlacedVersionFact, PlacedVersionFact, bool> differs) =>
        new(part, differs, static (_, _) => true);

    public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope, Func<PluginName, bool> stopsTheWalk)
    {
        StopsTheWalk = stopsTheWalk;
        var walked = records.Select(record => Walk(record, scope.Target, stopsTheWalk)).ToImmutableArray();
        return new(
            walked,
            walked.Length,
            overridden,
            links,
            NavmeshCount: 0,
            UnnamedCellsOf(walked),
            LinkedFrom,
            ChangedCells: [],
            ChangedLands: [],
            ChangedNavmeshes: [],
            DeletedCells: new HashSet<RecordKey>(),
            OverriddenDoorLinks: new Dictionary<RecordKey, DoorLinkFact>(),
            CellsDeletedInFinalWorld: new Dictionary<RecordKey, PluginName>(),
            Spaces: walked.Select(record => record.Space).DistinctBy(space => space.Key).ToDictionary(space => space.Key));
    }

    /// <summary>
    /// As the real reader keeps them: the versions from the winner down to and including the first one whose plugin stops the walk.
    /// A record the target plugin overrides is walked on down to its version just below the target plugin's, and its parts are
    /// judged from there up, unless the record comes with its changes.
    /// </summary>
    private PlacedRecordFact Walk(PlacedRecordFact record, PluginName target, Func<PluginName, bool> stopsTheWalk)
    {
        var versions = record.Versions;
        var targetIndex = versions.TakeWhile(version => version.Plugin != target).Count();
        var isJudged = overridden.Contains(record.Key) && targetIndex > 0 && targetIndex < versions.Length;
        var lowestKept = isJudged ? targetIndex - 1 : versions.Length - 1;
        while (lowestKept > 0 && !stopsTheWalk(versions[lowestKept].Plugin)) lowestKept--;
        return record with
        {
            Versions = versions.RemoveRange(0, lowestKept),
            CleanedModChanges = record.CleanedModChanges ?? (isJudged ? ChangesOf(versions[(targetIndex - 1)..]) : null),
        };
    }

    /// <param name="versions">From the version just below the target plugin's up to the winner.</param>
    private static PlacedPartChanges ChangesOf(ImmutableArray<PlacedVersionFact> versions) =>
        PlacedPartJudging.Judge(VersionParts, versions, [.. versions.Select(version => version.Plugin)], TestTargets.PatchName);

    /// <summary>Each cell of the records once, without grid, name or Location.</summary>
    private static Dictionary<RecordKey, CellAreaFact> UnnamedCellsOf(ImmutableArray<PlacedRecordFact> records) =>
        records
            .DistinctBy(record => record.Cell.Key)
            .ToDictionary(record => record.Cell.Key, record => new CellAreaFact(record.Space.Key, Grid: null, Name: null, Location: null));

    /// <summary>The records the placed records of the target spaces link to.</summary>
    public IReadOnlySet<RecordKey> LinkedFrom { get; init; } = new HashSet<RecordKey>();

    /// <summary>The records the non-placed read reports as unreadable.</summary>
    public ImmutableArray<UnreadableRecord> Unreadable { get; init; } = [];

    public NonPlacedLinks ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets, IReadOnlySet<PluginName> linkedPlugins) => new([], Unreadable);

    public IWritableRecords ReadWritableRecords() => throw new NotSupportedException();

    public IRecordIndex ReadRecordIndex() => throw new NotSupportedException();

    public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

    public float[]? ReadTerrain(ExteriorCell cell) => throw new NotSupportedException();

    public float[]? ReadTerrainBelowTarget(ExteriorCell cell) => throw new NotSupportedException();

    public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => throw new NotSupportedException();

    public WaterCopies ReadWaterCopies(ExteriorCell cell) => throw new NotSupportedException();
    public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => throw new NotSupportedException();

    public RecordKey? FindLandWorldspace(RecordKey space) => throw new NotSupportedException();

    public NavmeshWorldTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

    public NpcTraits? NpcTraitsOf(BaseKey npc) => throw new NotSupportedException();

    public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

    public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

    public bool TryParsePluginName(string text, out PluginName name) => throw new NotSupportedException();
}
