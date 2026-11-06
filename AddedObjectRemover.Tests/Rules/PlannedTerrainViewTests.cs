using System.Collections.Immutable;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The terrain and water a restore pass plans, as the next patcher reads them: in a cell the pass writes, the patch's version is a copy
/// of the winning version with the heights as the height map stores them and the earlier version's layers in the quarters going back.
/// </summary>
public sealed class PlannedTerrainViewTests
{
    private static readonly PluginName BaseGame = new("Skyrim.esm");
    private static readonly PluginName CleanedMod = TestTargets.TargetName;
    private static readonly PluginName Patch = TestTargets.PatchName;
    private static readonly ExteriorCell Cell = new(TestTargets.Space, 0, 0);
    private static readonly ExteriorCell OtherCell = new(TestTargets.Space, 1, 0);
    private static readonly RecordKey Road = new(BaseGame, 0x300);
    private static readonly RecordKey Grass = new(CleanedMod, 0x301);
    private static readonly RecordKey Sand = new(Patch, 0x302);

    [Fact]
    public void ThePatchsVersionOfALandRecordThePassWritesIsAddedLastWithTheStoredHeightsAndTheEarlierLayersOfTheQuartersGoingBack()
    {
        var planned = Heights(12.3f);
        var view = ViewOver([Version(BaseGame, 0f, Road), Version(CleanedMod, 80f, Grass)], new PlannedLand(planned, QuarterSet.Of([0, 3]), Rest: false));

        var versions = view.ReadTerrainVersions(Cell);

        Assert.Equal([BaseGame, CleanedMod, Patch], versions.Select(version => version.Plugin));
        Assert.Equal(LandHeightEncoder.Stored(planned), versions[^1].Heights);
        Assert.Equal([Road, Grass, Grass, Road], versions[^1].Layers!.Quadrants.Select(quadrant => quadrant.Base));
    }

    [Fact]
    public void ThePatchsOwnVersionIsReplacedAndKeepsWhatThePassDoesNotWrite()
    {
        var view = ViewOver([Version(BaseGame, 0f, Road), Version(CleanedMod, 80f, Grass), Version(Patch, 40f, Sand)], new PlannedLand(Heights: null, QuarterSet.Of([1]), Rest: false));

        var versions = view.ReadTerrainVersions(Cell);

        Assert.Equal([BaseGame, CleanedMod, Patch], versions.Select(version => version.Plugin));
        Assert.Equal(Heights(40f), versions[^1].Heights);
        Assert.Equal([Sand, Road, Sand, Sand], versions[^1].Layers!.Quadrants.Select(quadrant => quadrant.Base));
    }

    [Fact]
    public void ACellThePassDoesNotWriteReadsAsTheLoadOrder()
    {
        TerrainVersion[] read = [Version(BaseGame, 0f, Road), Version(CleanedMod, 80f, Grass)];

        Assert.Same(read, ViewOver(read, new PlannedLand(Heights(0f), QuarterSet.Of([0]), Rest: false)).ReadTerrainVersions(OtherCell));
    }

    /// <summary>The rest going back copies the height map the earlier version holds and the winner does not, or drops the winner's.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheRestGoingBackGivesTheHeightMapOfTheEarlierVersionWhereOnlyOneOfTheTwoHoldsOne(bool earlierHasHeights)
    {
        TerrainVersion[] read =
        [
            new(BaseGame, earlierHasHeights ? Heights(0f) : null, Layers: null),
            new(CleanedMod, earlierHasHeights ? null : Heights(80f), Layers: null),
        ];

        var versions = ViewOver(read, new PlannedLand(Heights: null, QuarterSet.Of([]), Rest: true)).ReadTerrainVersions(Cell);

        Assert.Equal(earlierHasHeights ? Heights(0f) : null, versions[^1].Heights);
    }

    /// <summary>
    /// The writer copies the rest first, which drops the height map the earlier version lacks, and then writes the planned heights into
    /// a new one, so the planned heights as stored are what the record holds.
    /// </summary>
    [Fact]
    public void PlannedHeightsAreStoredWhenTheRestGoesBackFromAVersionWithoutAHeightMap()
    {
        var planned = Heights(12.3f);
        TerrainVersion[] read = [new(BaseGame, Heights: null, Layers: null), Version(CleanedMod, 80f, Grass)];

        var versions = ViewOver(read, new PlannedLand(planned, QuarterSet.Of([]), Rest: true)).ReadTerrainVersions(Cell);

        Assert.Equal(LandHeightEncoder.Stored(planned), versions[^1].Heights);
    }

    [Fact]
    public void TheRestGoingBackKeepsTheWinnersHeightsWhereBothHoldAHeightMap()
    {
        var versions = ViewOver([Version(BaseGame, 0f, Road), Version(CleanedMod, 80f, Grass)], new PlannedLand(Heights: null, QuarterSet.Of([]), Rest: true)).ReadTerrainVersions(Cell);

        Assert.Equal(Heights(80f), versions[^1].Heights);
    }

    [Fact]
    public void TheWaterGoingBackIsThePatchsCopyAddedLast()
    {
        var loadOrder = new FixedView([], new WaterCopies([new CellWaterCopy(BaseGame, HasWater: true, 50f), new CellWaterCopy(CleanedMod, HasWater: true, -100f)], []));
        var view = new PlannedTerrainView(
            loadOrder, Patch, CleanedMod, new Dictionary<ExteriorCell, PlannedLand>(), new Dictionary<ExteriorCell, CellWaterCopy> { [Cell] = new(BaseGame, HasWater: true, 50f) }, NothingGivenBack());

        Assert.Equal(new CellWaterCopy(Patch, HasWater: true, 50f), view.ReadWaterCopies(Cell).Cell[^1]);
        Assert.Equal(2, view.ReadWaterCopies(OtherCell).Cell.Length);
    }

    [Fact]
    public void ThePartsThePassGivesBackFollowThoseEarlierRunsGaveBackInTheirCellOnly()
    {
        var earlier = new RestoredPart(new PluginName("Earlier.esp"), new RecordKey(BaseGame, 0x400), new RestoredPartName.LandHeights(PointSet.Of([0])), BaseGame);
        var planned = new RestoredPart(CleanedMod, new RecordKey(BaseGame, 0x401), new RestoredPartName.LandTextureQuarter(2), BaseGame);
        var loadOrder = new FixedView([], new WaterCopies([], []), [earlier]);
        var view = new PlannedTerrainView(
            loadOrder, Patch, CleanedMod, new Dictionary<ExteriorCell, PlannedLand>(), new Dictionary<ExteriorCell, CellWaterCopy>(), new[] { (Cell, planned) }.ToLookup(given => given.Cell, given => given.planned));

        Assert.Equal([earlier, planned], view.ReadRestoredParts(Cell).AsEnumerable());
        Assert.Equal([earlier], view.ReadRestoredParts(OtherCell).AsEnumerable());
    }

    private static ILookup<ExteriorCell, RestoredPart> NothingGivenBack() => Array.Empty<RestoredPart>().ToLookup(_ => Cell);

    private static PlannedTerrainView ViewOver(TerrainVersion[] versions, PlannedLand land) =>
        new(new FixedView(versions, new WaterCopies([], [])), Patch, CleanedMod, new Dictionary<ExteriorCell, PlannedLand> { [Cell] = land }, new Dictionary<ExteriorCell, CellWaterCopy>(), NothingGivenBack());

    private static TerrainVersion Version(PluginName plugin, float height, RecordKey texture) => new(plugin, Heights(height), TestTerrainView.PaintedWith(texture));

    private static float[] Heights(float height) => [.. Enumerable.Repeat(height, TerrainGrid.VertexCount)];

    /// <summary>Every cell holds the same versions, water copies and parts earlier runs gave back.</summary>
    private sealed class FixedView(TerrainVersion[] versions, WaterCopies water, ImmutableArray<RestoredPart> restored = default) : ITerrainView
    {
        public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => versions;

        public WaterCopies ReadWaterCopies(ExteriorCell cell) => water;

        public ImmutableArray<RestoredPart> ReadRestoredParts(ExteriorCell cell) => restored.IsDefault ? [] : restored;

        public RecordKey[] ReadTextureMaterialChain(RecordKey texture) => [texture];

        public RecordKey? FindLandWorldspace(RecordKey space) => space;
    }
}
