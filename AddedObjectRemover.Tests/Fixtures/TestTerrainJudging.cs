using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>
/// A changed-terrain rule that gives every object it checks the same outcome over the load order as read, and another over any other
/// view, and records what it checks.
/// </summary>
/// <param name="baseline">The outcome of every check over the load order as read.</param>
/// <param name="planned">The outcome of every check over any other view.</param>
/// <param name="reads">The cells every object's check reads; none when null.</param>
internal sealed class TestTerrainJudging(TerrainCheckOutcome baseline, TerrainCheckOutcome planned = default, IReadOnlySet<ExteriorCell>? reads = null) : IChangedTerrainJudging
{
    /// <summary>A rule that finds nothing and reads no terrain, for scenes that do not judge terrain.</summary>
    public static TestTerrainJudging FindsNothing => new(default);

    /// <summary>Every object checked over the load order as read, in no particular order.</summary>
    public ConcurrentQueue<TargetObject> Checked { get; } = new();

    /// <summary>Every object checked over another view, in no particular order.</summary>
    public ConcurrentQueue<TargetObject> CheckedOverPlans { get; } = new();

    public ITerrainView LoadOrder => NoTerrain.View;

    public IObjectTerrainChecks Over(ITerrainView view) => view == LoadOrder ? new Checks(baseline, Checked) : new Checks(planned, CheckedOverPlans);

    public ImmutableArray<int> FindObjectsReading(IReadOnlyList<(TargetObject Object, ChangedTerrainSettings Settings)> objects, IReadOnlySet<ExteriorCell> cells) =>
        reads is { } read && read.Overlaps(cells) ? [.. Enumerable.Range(0, objects.Count)] : [];

    public IReadOnlySet<ExteriorCell> FindCellsRead(TargetObject target, ChangedTerrainSettings settings) => reads ?? new HashSet<ExteriorCell>();

    private sealed class Checks(TerrainCheckOutcome outcome, ConcurrentQueue<TargetObject> checkedObjects) : IObjectTerrainChecks
    {
        public TerrainCheckOutcome Check(TargetObject target, ChangedTerrainSettings settings)
        {
            checkedObjects.Enqueue(target);
            return outcome;
        }
    }

    /// <summary>A view no check of this rule reads.</summary>
    private sealed class NoTerrain : ITerrainView
    {
        public static NoTerrain View { get; } = new();

        public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => throw new NotSupportedException();

        public WaterCopies ReadWaterCopies(ExteriorCell cell) => throw new NotSupportedException();

        public ImmutableArray<RestoredPart> ReadRestoredParts(ExteriorCell cell) => throw new NotSupportedException();

        public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => throw new NotSupportedException();

        public RecordKey? FindLandWorldspace(RecordKey space) => throw new NotSupportedException();
    }
}

/// <summary>
/// The terrain and water of in-memory scenes: each LAND record the cleaned mod changed has the version just below the cleaned mod's and
/// the cleaned mod's, and each cell record whose water it changed has the earlier water and the cleaned mod's. Every space uses its own
/// land, and every texture is its own material.
/// </summary>
/// <param name="lands">With the heights of both versions.</param>
/// <param name="earlierPlugin">The plugin of the LAND records' version below the cleaned mod's.</param>
/// <param name="layers">The texture layers of a LAND record's version by its plugin; none when null.</param>
/// <param name="otherWater">The water copies, in load order, of cells whose water the cleaned mod did not change.</param>
internal sealed class TestTerrainView(
    IEnumerable<LandChangedByCleanedMod> lands,
    IEnumerable<CellChangedByCleanedMod> cells,
    PluginName earlierPlugin,
    PluginName cleanedMod,
    Func<PluginName, TerrainLayers?> layers,
    IReadOnlyDictionary<ExteriorCell, CellWaterCopy[]> otherWater) : ITerrainView
{
    /// <summary>The first record of several at one cell.</summary>
    private readonly Dictionary<ExteriorCell, LandChangedByCleanedMod> _lands = lands.DistinctBy(land => land.Cell).ToDictionary(land => land.Cell);

    /// <summary>The first record of several at one grid.</summary>
    private readonly Dictionary<ExteriorCell, CellChangedByCleanedMod> _cells = cells
        .Where(cell => cell.Grid is not null)
        .DistinctBy(cell => (cell.SpaceKey, cell.Grid))
        .ToDictionary(cell => new ExteriorCell(cell.SpaceKey, cell.Grid!.Value.X, cell.Grid.Value.Y));

    /// <summary>Every quadrant of a version painted with one texture, which is the plugin's own material.</summary>
    public static TerrainLayers PaintedWith(RecordKey texture) => new([.. Enumerable.Range(0, TerrainQuadrants.Count).Select(_ => new QuadrantLayers(texture, []))]);

    public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) =>
        _lands.TryGetValue(cell, out var land) && land.Heights is { } heights
            ? [new TerrainVersion(earlierPlugin, heights.Earlier, layers(earlierPlugin)), new TerrainVersion(cleanedMod, heights.Current, layers(cleanedMod))]
            : [];

    public WaterCopies ReadWaterCopies(ExteriorCell cell) =>
        new(_cells.TryGetValue(cell, out var changed) ? [changed.EarlierWater, changed.CurrentWater] : otherWater.GetValueOrDefault(cell, []), []);

    /// <summary>No earlier run gave anything back.</summary>
    public ImmutableArray<RestoredPart> ReadRestoredParts(ExteriorCell cell) => [];

    public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => [texture];

    public RecordKey? FindLandWorldspace(RecordKey space) => space;
}
