using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>The area lines: how a target object's worldspace, hold and place are named, ranked and folded in each log.</summary>
public sealed class AreaLinesTests
{
    private static readonly ReportContext Normal = new(null!, null!, Detailed: false);
    private static readonly ReportContext Detailed = new(null!, null!, Detailed: true);
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
    public void NormalLog_NamesTheTopFiveOfEachLevelAndFoldsTheRest()
    {
        var lines = LogSections.BuildAreasSection(World(), Removals(), [], [], NoRisks, Normal).Lines.ToList();

        Assert.Equal(
            [
                "Removed 8 objects in:",
                "  worldspaces: 7 Skyrim, 1 Bannered Mare.",
                "  holds: 3 The Pale (Skyrim), 2 Falkreath (Skyrim), 1 Whiterun (Bannered Mare), 1 Whiterun (Skyrim), 1 unnamed area (Skyrim).",
                "  places: 2 Loreius Farm (Skyrim), 1 A (Skyrim), 1 B (Skyrim), 1 Bannered Mare, 1 Dawnstar (Skyrim); 2 more in 2 other places.",
            ],
            lines);
    }

    [Fact]
    public void NormalLog_GivesEachNonEmptySummaryItsOwnLines()
    {
        var kept = new KeptObject(2, TestKeepReasons.Quest, TouchedTargetIndex: null);
        var hint = new ManualPatchHint(ManualPatchHintType.KeptForNonPlacedReference, 2, "detail");
        var risks = new RemovalRisks([], [new TargetId(5), new TargetId(2)]);

        var lines = LogSections.BuildAreasSection(World(), Removals(), [kept, kept], [hint], risks, Normal).Lines.ToList();

        Assert.Equal(
            [
                "Kept 1 objects that would have been removed, in:",
                "  worldspaces: 1 Skyrim.",
                "  holds: 1 The Pale (Skyrim).",
                "  places: 1 Loreius Farm (Skyrim).",
                "Possible manual patch needed for 1 objects, in:",
                "  worldspaces: 1 Skyrim.",
                "  holds: 1 The Pale (Skyrim).",
                "  places: 1 Loreius Farm (Skyrim).",
                "Removed 1 objects of the large-reference lists, in:",
                "  worldspaces: 1 Skyrim.",
                "  holds: 1 unnamed area (Skyrim).",
                "  places: 1 cell 5,7 (Skyrim).",
            ],
            lines.Skip(4).ToList());
    }

    [Fact]
    public void DetailedLog_ListsEveryAreaAndForTheRemovalsTheTargetObjectsPerHold()
    {
        var hint = new ManualPatchHint(ManualPatchHintType.KeptForNonPlacedReference, 2, "detail");

        var lines = LogSections.BuildAreasSection(World(), Removals(), [], [hint], NoRisks, Detailed).Lines.ToList();

        Assert.Equal(
            [
                "Removed 8 objects in:",
                "  worldspaces:",
                "    7 Skyrim",
                "    1 Bannered Mare",
                "  holds (removed / target objects there):",
                "    3 / 4 The Pale (Skyrim)",
                "    2 / 2 Falkreath (Skyrim)",
                "    1 / 1 Whiterun (Bannered Mare)",
                "    1 / 1 Whiterun (Skyrim)",
                "    1 / 1 unnamed area (Skyrim)",
                "  places:",
                "    2 Loreius Farm (Skyrim)",
                "    1 A (Skyrim)",
                "    1 B (Skyrim)",
                "    1 Bannered Mare",
                "    1 Dawnstar (Skyrim)",
                "    1 cell 5,7 (Skyrim)",
                "    1 near Halted Stream Camp (Skyrim)",
                "Possible manual patch needed for 1 objects, in:",
                "  worldspaces:",
                "    1 Skyrim",
                "  holds:",
                "    1 The Pale (Skyrim)",
                "  places:",
                "    1 Loreius Farm (Skyrim)",
            ],
            lines);
    }

    [Fact]
    public void NoObjectsGiveNoLines()
    {
        Assert.Empty(LogSections.BuildAreasSection(World(), [], [], [], NoRisks, Normal).Lines);
    }

    [Fact]
    public void EqualNamesInDifferentSpacesStayApartAndRankByName()
    {
        var world = World();
        var ranked = AreaGroups.Rank(world, [4, 8], AreaLevel.Hold);

        Assert.Equal(["Whiterun (Bannered Mare)", "Whiterun (Skyrim)"], ranked.Select(entry => entry.Key.Name));
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
