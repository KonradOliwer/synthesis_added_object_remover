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
                "Removed 8 objects in:",
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

        Assert.Equal(["Removed 1 objects in:", "  Bannered Mare › Whiterun hold 1/1"], lines);
    }

    [Fact]
    public void EachNonEmptySummaryGetsItsOwnTree()
    {
        var kept = new KeptObject(2, TestKeepReasons.Quest, TouchedTargetIndex: null);
        var keptOfAnotherKind = new KeptObject(4, TestKeepReasons.NonPlaced("FLST", "MyList"), TouchedTargetIndex: null);
        var hint = new ManualPatchHint(ManualPatchHintType.KeptForNonPlacedReference, 2, "detail");
        var risks = new RemovalRisks([], [new TargetId(5), new TargetId(2)]);

        var lines = LogSections.BuildAreasSection(World(), Removals(), [kept, kept, keptOfAnotherKind], [hint], risks).Lines.ToList();

        Assert.Equal(
            [
                "Kept 1 objects that would have been removed, FLST record, in:",
                "  Skyrim › Whiterun hold › near Halted Stream Camp 1/1",
                "Kept 1 objects that would have been removed, QUST record, in:",
                "  Skyrim › The Pale hold › Loreius Farm 1/3",
                "Possible manual patch needed for 1 objects, in:",
                "  Skyrim › The Pale hold › Loreius Farm 1/3",
                "Removed 1 objects of the large-reference lists, in:",
                "  Skyrim › unnamed area › cell 5,7 1/1",
            ],
            lines.Skip(11).ToList());
    }

    [Fact]
    public void NoObjectsGiveNoLines()
    {
        Assert.Empty(LogSections.BuildAreasSection(World(), [], [], [], NoRisks).Lines);
    }

    private static TargetArea Exterior(string? hold, string? place, PlaceSource source, int x, int y) => new(hold, place, source, new CellGridPoint(x, y));

    /// <summary>Removes every target object but 2.</summary>
    private static List<RemovedObject> Removals() => [.. new[] { 0, 1, 3, 4, 5, 6, 7, 8 }.Select(index => new LinkedRemoval(index, 0))];

    /// <summary>Targets 0-2 at Loreius Farm, 3 in Dawnstar, 4 near a camp, 5 unnamed, 6-7 in Falkreath, 8 in an interior.</summary>
    private static CollectedObjects World()
    {
        TargetArea[] areas =
        [
            Exterior("The Pale", "Loreius Farm", PlaceSource.OwnLocation, 7, 6),
            Exterior("The Pale", "Loreius Farm", PlaceSource.OwnLocation, 7, 6),
            Exterior("The Pale", "Loreius Farm", PlaceSource.OwnLocation, 7, 6),
            Exterior("The Pale", "Dawnstar", PlaceSource.OwnLocation, 8, 6),
            Exterior("Whiterun", "Halted Stream Camp", PlaceSource.NearbyLocation, 1, 1),
            Exterior(null, null, PlaceSource.GridOnly, 5, 7),
            Exterior("Falkreath", "A", PlaceSource.OwnLocation, 2, 2),
            Exterior("Falkreath", "B", PlaceSource.CellName, 3, 3),
            new TargetArea("Whiterun", "Bannered Mare", PlaceSource.CellName, Grid: null),
        ];
        List<TargetObject> targets =
        [
            .. areas.Select((area, index) =>
                TestTargets.Create(index, TestTargets.At(Vector3.Zero), baseKey: null, area.Grid is null ? Interior : TestTargets.Space) with { Area = area }),
        ];
        return new CollectedObjects(
            [.. targets],
            [],
            [],
            false,
            [],
            new Dictionary<RecordKey, SpaceFact> { [Skyrim.Key] = Skyrim, [Interior] = BanneredMare },
            new ReadCounts(0, 0, 0, 0, 0, 0, 0, 0),
            [],
            TargetWorkOrder.Of(targets),
            null);
    }
}
