using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>The area lines: how a target object's worldspace, hold and place are named, and the tree they form in the log.</summary>
public sealed class AreaLinesTests
{
    private static readonly RecordKey Interior = TestTargets.SpaceKey(0x101);
    private static readonly SpaceFact Skyrim = new(TestTargets.Space, "Tamriel", SpaceKind.Worldspace, "Skyrim");
    private static readonly SpaceFact BanneredMare = new(Interior, "WhiterunBanneredMare", SpaceKind.Interior, "Bannered Mare");
    private static readonly RemovalRisks NoRisks = new([], []);

    [Fact]
    public void Place_IsTheNameThenNearTheBorrowedNameThenTheGridCellThenTheSpace()
    {
        Assert.Equal("Loreius Farm", AreaText.Place(Exterior("The Pale", "Loreius Farm", PlaceSource.OwnLocation, 7, 6), Skyrim));
        Assert.Equal("near Loreius Farm", AreaText.Place(Exterior("The Pale", "Loreius Farm", PlaceSource.NearbyLocation, 7, 6), Skyrim));
        Assert.Equal("cell -3,9", AreaText.Place(Exterior(null, null, PlaceSource.GridOnly, -3, 9), Skyrim));
        Assert.Equal("Bannered Mare", AreaText.Place(new TargetArea(null, null, PlaceSource.CellName, Grid: null), BanneredMare));
    }

    [Fact]
    public void Worldspace_IsTheDisplayNameElseTheEditorIdLabel()
    {
        Assert.Equal("Skyrim", AreaText.Worldspace(Skyrim));
        Assert.Equal("Tamriel [000100:Target.esp]", AreaText.Worldspace(Skyrim with { Name = null }));
    }

    [Fact]
    public void Detail_NamesPlaceHoldAndGridOfAnExteriorAndNoGridForAnInterior()
    {
        Assert.Equal("place Loreius Farm, hold The Pale, grid 7,6", AreaText.Detail(Exterior("The Pale", "Loreius Farm", PlaceSource.OwnLocation, 7, 6), Skyrim));
        Assert.Equal("place Bannered Mare, hold unnamed area", AreaText.Detail(new TargetArea(null, "Bannered Mare", PlaceSource.CellName, Grid: null), BanneredMare));
    }

    [Fact]
    public void HoldLabel_AddsHoldUnlessTheNameEndsInItOrTheAreaIsUnnamed()
    {
        Assert.Equal("The Pale hold", AreaText.HoldLabel(Exterior("The Pale", "Dawnstar", PlaceSource.OwnLocation, 0, 0)));
        Assert.Equal("Falkreath Hold", AreaText.HoldLabel(Exterior("Falkreath Hold", "A", PlaceSource.OwnLocation, 0, 0)));
        Assert.Equal("unnamed area", AreaText.HoldLabel(Exterior(null, null, PlaceSource.GridOnly, 0, 0)));
    }

    [Fact]
    public void Removals_AreOneTreeWithTheRemovedAndTargetObjectsOfEachAreaAndSingleChildrenJoined()
    {
        var lines = LogSections.BuildAreasSection(World(), Removals(), [], [], NoRisks).Lines.ToList();

        Assert.Equal(
            [
                "Removed 8/9 (removed/all checked objects):",
                "  Skyrim 7/8",
                "    The Pale hold 3/4",
                "      Loreius Farm 2/3",
                "      Dawnstar 1/1",
                "    Falkreath hold 2/2",
                "      A 1/1",
                "      B 1/1",
                "    Whiterun hold › near Halted Stream Camp 1/1",
                "    unnamed area › cell 5,7 1/1",
                "  Bannered Mare › Whiterun hold 1/1",
            ],
            lines);
    }

    [Fact]
    public void ANameThatRepeatsAnEarlierNameOfAJoinedChainIsLeftOut()
    {
        var lines = LogSections.BuildAreasSection(World(), [new LinkedRemoval(8, 0)], [], [], NoRisks).Lines.ToList();

        Assert.Equal(["Removed 1/9 (removed/all checked objects):","  Bannered Mare › Whiterun hold 1/1"], lines);
    }

    [Fact]
    public void EachNonEmptySummaryGetsItsOwnTree()
    {
        var kept = new KeptObject(2, TestKeepReasons.Quest, TouchedTargetIndex: null);
        var keptOfAnotherKind = new KeptObject(4, TestKeepReasons.NonPlaced("FLST", "MyList"), TouchedTargetIndex: null);
        var hint = new ManualPatchHint(ManualPatchHintType.KeptForNonPlacedReference, 2, "detail");
        var markerHint = new ManualPatchHint(ManualPatchHintType.RemovedMarker, 2, "DoorMarkers");
        var risks = new RemovalRisks([], [new TargetId(5), new TargetId(2)]);

        var lines = LogSections.BuildAreasSection(World(), Removals(), [kept, kept, keptOfAnotherKind], [markerHint, hint], risks).Lines.ToList();

        Assert.Equal(
            [
                "Kept 1/9 (kept/all checked objects), FLST record:",
                "  Skyrim › Whiterun hold › near Halted Stream Camp 1/1",
                "Kept 1/9 (kept/all checked objects), QUST record:",
                "  Skyrim › The Pale hold › Loreius Farm 1/3",
                "Removed markers that actors or the map use: 1 of 9:",
                "  Skyrim › The Pale hold › Loreius Farm 1/3",
                "Kept objects that would have been removed, to check by hand: 1 of 9:",
                "  Skyrim › The Pale hold › Loreius Farm 1/3",
                "Removed 1/9 (large-reference list objects/all checked objects):",
                "  Skyrim › unnamed area › cell 5,7 1/1",
            ],
            lines.Skip(11).ToList());
    }

    [Fact]
    public void SiblingsWithTheSameCountPutTheOneWithFewerTargetObjectsFirst()
    {
        var world = WorldOf(
            Place("Big"), Place("Big"), Place("Big"),
            Place("Small"),
            Place("Alpha"), Place("Alpha"));

        var lines = LogSections.BuildAreasSection(world, Removed(0, 3, 4), [], [], NoRisks).Lines.ToList();

        Assert.Equal(
            [
                "Removed 3/6 (removed/all checked objects):",
                "  Skyrim › H hold 3/6",
                "      Small 1/1",
                "      Alpha 1/2",
                "      Big 1/3",
            ],
            lines);
    }

    [Fact]
    public void APlaceAndItsNearPlaceStayTogetherRankedByTheirCombinedNumbers()
    {
        var world = WorldOf(
            Place("Zed"),
            NearPlace("Zed"), NearPlace("Zed"),
            Place("Mid"), Place("Mid"), Place("Mid"), Place("Mid"), Place("Mid"),
            NearPlace("Solo"), NearPlace("Solo"));

        var lines = LogSections.BuildAreasSection(world, Removed(0, 1, 2, 3, 4, 8, 9), [], [], NoRisks).Lines.ToList();

        Assert.Equal(
            [
                "Removed 7/10 (removed/all checked objects):",
                "  Skyrim › H hold 7/10",
                "      Zed 1/1",
                "      near Zed 2/2",
                "      near Solo 2/2",
                "      Mid 2/5",
            ],
            lines);
    }

    [Fact]
    public void NoObjectsGiveNoLines()
    {
        Assert.Empty(LogSections.BuildAreasSection(World(), [], [], [], NoRisks).Lines);
    }

    private static TargetArea Exterior(string? hold, string? place, PlaceSource source, int x, int y) => new(hold, place, source, new CellGridPoint(x, y));

    private static TargetArea Place(string place) => Exterior("H", place, PlaceSource.OwnLocation, 1, 1);

    private static TargetArea NearPlace(string place) => Exterior("H", place, PlaceSource.NearbyLocation, 1, 1);

    private static List<RemovedObject> Removed(params int[] targetIndices) => [.. targetIndices.Select(index => new LinkedRemoval(index, 0))];

    /// <summary>Removes every target object but 2.</summary>
    private static List<RemovedObject> Removals() => Removed(0, 1, 3, 4, 5, 6, 7, 8);

    /// <summary>Targets 0-2 at Loreius Farm, 3 in Dawnstar, 4 near a camp, 5 unnamed, 6-7 in Falkreath, 8 in an interior.</summary>
    private static CollectedObjects World() =>
        WorldOf(
            Exterior("The Pale", "Loreius Farm", PlaceSource.OwnLocation, 7, 6),
            Exterior("The Pale", "Loreius Farm", PlaceSource.OwnLocation, 7, 6),
            Exterior("The Pale", "Loreius Farm", PlaceSource.OwnLocation, 7, 6),
            Exterior("The Pale", "Dawnstar", PlaceSource.OwnLocation, 8, 6),
            Exterior("Whiterun", "Halted Stream Camp", PlaceSource.NearbyLocation, 1, 1),
            Exterior(null, null, PlaceSource.GridOnly, 5, 7),
            Exterior("Falkreath", "A", PlaceSource.OwnLocation, 2, 2),
            Exterior("Falkreath", "B", PlaceSource.CellName, 3, 3),
            new TargetArea("Whiterun", "Bannered Mare", PlaceSource.CellName, Grid: null));

    /// <summary>One target object per area, in order; an area without a grid is in the interior.</summary>
    private static CollectedObjects WorldOf(params TargetArea[] areas)
    {
        List<TargetObject> targets =
        [
            .. areas.Select((area, index) =>
                TestTargets.Create(index, TestTargets.At(Vector3.Zero), baseKey: null, area.Grid is null ? Interior : TestTargets.Space) with { Area = area }),
        ];
        return new CollectedObjects(
            [.. targets],
            [],
            [],
            [],
            [],
            new Dictionary<RecordKey, SpaceFact> { [Skyrim.Key] = Skyrim, [Interior] = BanneredMare },
            new ReadCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            [],
            TargetWorkOrder.Of(targets),
            null);
    }
}
