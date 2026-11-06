using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>
/// The log and the report files of the step that restores what the cleaned mod changed where it was removed: the counts, the
/// reasons in plain words, the lines per change, the summary item and the two tables, over results built by hand.
/// </summary>
public sealed class RestoreWhereRemovedReportTests
{
    private const string Heading = "Restored where the cleaned mod was removed";
    private const int HeightRowLength = TerrainGrid.VerticesPerSide;

    private static readonly ReportContext Normal = new(Bases: null!, Shapes: null!, Detailed: false);
    private static readonly ReportContext Detailed = new(Bases: null!, Shapes: null!, Detailed: true);
    private static readonly TimeSpan Elapsed = TimeSpan.FromSeconds(1.5);

    private static readonly PluginName BaseGame = new("Base.esm");
    private static readonly PluginName Later = new("Later.esp");
    private static readonly PluginName Patch = new("Patch.esp");
    private static readonly RecordKey WaterObject = new(Later, 0x777);

    private static readonly AreaShare Cleared = new(Objects: 214, Standing: 0, Size: 8_000_000, StandingSize: 0, AreaVerdict.Cleared, CountPercent: 0, SizePercent: 0);
    private static readonly AreaShare StillStanding = new(Objects: 10, Standing: 4, Size: 1000, StandingSize: 250, AreaVerdict.StillStanding, CountPercent: 40, SizePercent: 25);
    private static readonly AreaShare NoObjects = new(Objects: 0, Standing: 0, Size: 0, StandingSize: 0, AreaVerdict.NoObjectsAround, CountPercent: 0, SizePercent: 0);

    private static readonly RestoreWhereRemovedResult SwitchedOff = RestoreWhereRemovedResult.SwitchedOff();

    private static string Space => $"worldspace {RecordLabels.Of(TestTargets.Space, "Tamriel")}";

    // ---- the normal log

    [Fact]
    public void TheNormalLogCountsWhatWentBackPerKindOfChange()
    {
        var lines = Section(Scene(), Normal).Lines;

        Assert.Equal(
            $"{Heading}: 4 objects of other mods (1 with their own model again, 1 shown again, 1 hidden again, 1 moved back), "
            + "terrain in 1 cell quarter of 1 cell, settings of 2 cells (1 lighting, 2 music), "
            + "3 navmeshes (1 restored, 1 brought back, 1 of the cleaned mod's sunk under the world).",
            lines[0]);
    }

    [Fact]
    public void TheNormalLogCountsWhatStaysBecauseAGuardFoundSomethingThatStays()
    {
        var lines = Section(Scene(), Normal).Lines;

        Assert.Equal(
            "Left as the cleaned mod changed them: 2 objects (1 too close to objects that stay, 1 with a check that stopped unexpectedly); "
            + "terrain at 1 point (1 under objects that stay); water in 1 cell; 1 navmesh (1 linked to navmeshes that stay).",
            lines[1]);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void ACountOfZeroIsLeftOutAndALineWithNoFactIsNotPrinted()
    {
        var lines = Section(ObjectsOnly(Restored(Rock(0x912, PlacedParts.Position), Cleared)), Normal).Lines;

        Assert.Equal($"{Heading}: 1 object of other mods (1 moved back).", Assert.Single(lines));
    }

    [Fact]
    public void AResultWithNothingJudgedPrintsNothing()
    {
        var nothing = SwitchedOff with { Outcome = StepOutcome.Performed };

        Assert.Empty(Section(nothing, Normal).Lines);
        Assert.Empty(Section(nothing, Detailed).Lines);
        Assert.Empty(LogSections.BuildRestoreWhereRemovedSummarySection(nothing).Lines);
        Assert.Empty(BuildTables(nothing, Normal));
    }

    [Fact]
    public void ASwitchedOffStepPrintsNothingAndAStoppedOneSaysItWasNotPerformed()
    {
        Assert.Empty(Section(SwitchedOff, Detailed).Lines);

        var stopped = SwitchedOff with { Outcome = StepOutcome.StoppedByUnexpectedError };

        Assert.Equal("Restore where removed: not performed because of an unexpected error.", Assert.Single(Section(stopped, Normal).Lines));
    }

    /// <summary>The counts are grouped by kind in a fixed order, so they do not depend on the order the changes came in.</summary>
    [Fact]
    public void TheCountLinesDoNotDependOnTheOrderOfTheJudgedChanges()
    {
        var scene = Scene();
        var reversed = scene with
        {
            Objects = [.. scene.Objects.Reverse()],
            Cells = [.. scene.Cells.Reverse()],
            Navmeshes = [.. scene.Navmeshes.Reverse()],
            NavmeshGroups = [.. scene.NavmeshGroups.Reverse()],
        };

        Assert.Equal(string.Join('\n', Section(scene, Normal).Lines), string.Join('\n', Section(reversed, Normal).Lines));
        Assert.Equal(
            Assert.Single(LogSections.BuildRestoreWhereRemovedSummarySection(scene).Lines),
            Assert.Single(LogSections.BuildRestoreWhereRemovedSummarySection(reversed).Lines));
    }

    // ---- the detailed log

    [Fact]
    public void TheDetailedLogCountsWhatALaterPluginChangedAgainAndWhatStaysForItsAreaOrBecauseItNeverGoesBack()
    {
        var lines = Section(Scene(), Detailed).Lines;

        Assert.Contains("Left to later plugins in the changes judged: 2 parts of 2 objects (Later.esp 1, Patch.esp 1), 1 part of 1 cell (Later.esp 1).", lines);
        Assert.Contains("Kept because the cleaned mod's objects still stand around them: 1 object, 1 cell, terrain in 1 cell quarter, 1 navmesh.", lines);
        Assert.Contains("Kept because the cleaned mod has no objects around them: 1 object, 0 cells.", lines);
        Assert.Contains("Not restored whatever the area around them: 2 changes (1 a plugin that loads after the patch has the last word on it, 1 a record of the cleaned mod links to it).", lines);
    }

    [Fact]
    public void TheDetailedLogHasALinePerObjectWithItsKindWhereItGoesAndTheAreaItWasJudgedIn()
    {
        var lines = Section(Scene(), Detailed).Lines;

        var movedBack = Assert.Single(lines, line => line.StartsWith("  Moved back: ", StringComparison.Ordinal));
        Assert.Equal(
            $"  Moved back: {RecordLabels.Of(Key(0x901), null)} (base (none)) in {Space} to (10, 20, 30); 0 of 214 objects still stand (0.0% by count, 0.0% by size); "
            + "left to later plugins: rotation (Later.esp)",
            movedBack);
        Assert.Contains(lines, line => line.StartsWith("  Shown again: ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("  Hidden again: ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("  Own model again: ", StringComparison.Ordinal));
    }

    [Fact]
    public void ARecordThatStaysIsSaidToStayWithItsReasonInPlainWords()
    {
        var lines = Section(Scene(), Detailed).Lines;

        Assert.Contains(
            $"  Kept as changed: {RecordLabels.Of(Key(0x905), null)} (base (none)) in {Space}: too close to objects that stay ({Key(0x500)}); 0 of 214 objects still stand (0.0% by count, 0.0% by size)",
            lines);
        Assert.Contains(
            $"  Kept as changed: {RecordLabels.Of(Key(0x906), null)} (base (none)) in {Space}: the check stopped unexpectedly (InvalidOperationException: broke); 0 of 214 objects still stand (0.0% by count, 0.0% by size)",
            lines);
        Assert.Contains(
            $"  Kept as changed: {RecordLabels.Of(Key(0x907), null)} (base (none)) in {Space}: the cleaned mod's objects still stand around it; 4 of 10 objects still stand (40.0% by count, 25.0% by size)",
            lines);
        Assert.Contains(
            $"  Kept as changed: {RecordLabels.Of(Key(0x908), null)} (base (none)) in {Space}: the cleaned mod has no objects around it",
            lines);
        Assert.Contains(
            $"  Kept as changed: {RecordLabels.Of(Key(0x909), null)} (base (none)) in {Space}: a plugin that loads after the patch has the last word on it (Later.esp)",
            lines);
    }

    [Fact]
    public void ATargetJudgedAtBothPlacesShowsBothAreas()
    {
        var both = ObjectsOnly(KeptForArea(Rock(0x910, PlacedParts.Position), Cleared, AreaVerdict.StillStanding, now: StillStanding));

        var line = Assert.Single(Section(both, Detailed).Lines, text => text.StartsWith("  Kept as changed", StringComparison.Ordinal));

        Assert.EndsWith("; at its old place 0 of 214 objects still stand (0.0% by count, 0.0% by size); where it stands now 4 of 10 objects still stand (40.0% by count, 25.0% by size)", line);
    }

    [Fact]
    public void TheVerbAgreesWithOneObjectStillStanding()
    {
        var one = StillStanding with { Objects = 1, Standing = 1, CountPercent = 100, SizePercent = 100 };

        var line = Assert.Single(
            Section(ObjectsOnly(KeptForArea(Rock(0x912, PlacedParts.Position), one, AreaVerdict.StillStanding)), Detailed).Lines,
            text => text.StartsWith("  Kept as changed", StringComparison.Ordinal));

        Assert.EndsWith("; 1 of 1 object still stands (100.0% by count, 100.0% by size)", line);
    }

    /// <summary>The cell's winning version loads after the patch and only carries the cleaned mod's values, so no part is left to a later plugin.</summary>
    [Fact]
    public void ACellKeptBecauseAPluginAfterThePatchHasTheLastWordNamesThatPluginInTheLogAndTheCellsFile()
    {
        var cell = Cell(9, 9, [CellPart.Region], area: null, never: NeverRestoredReason.WinnerLoadsAfterPatch);
        var scene = Scene() with { Objects = [], Cells = [cell with { Change = cell.Change with { WinningMod = Later } }], Lands = [], Navmeshes = [], NavmeshGroups = [] };

        var line = Assert.Single(Section(scene, Detailed).Lines, text => text.StartsWith("  Kept as changed", StringComparison.Ordinal));
        var table = Assert.Single(BuildTables(scene, Normal));

        Assert.Equal($"  Kept as changed: cell {Space}, cell (9, 9): a plugin that loads after the patch has the last word on it (Later.esp)", line);
        Assert.Equal("kept: a plugin that loads after the patch has the last word on it (Later.esp)", Field(table, Assert.Single(table.Rows), "result"));
    }

    [Fact]
    public void TerrainAndANavmeshKeptBecauseAPluginAfterThePatchHasTheLastWordNameThatPluginInTheLog()
    {
        var land = LandOutcomes.NeverRestored(Land().Change with { WinningMod = Later }, NeverRestoredReason.WinnerLoadsAfterPatch);
        var navmesh = Navmesh(0xA06, NavmeshCase.Edited, undo: null, Cleared);
        var neverNavmesh = navmesh with { Change = navmesh.Change with { WinningMod = Later }, Never = NeverRestoredReason.WinnerLoadsAfterPatch, Area = null };
        var scene = Scene() with { Objects = [], Cells = [], Lands = [land], Navmeshes = [neverNavmesh], NavmeshGroups = [] };

        var kept = Section(scene, Detailed).Lines.Where(text => text.StartsWith("  Kept as changed", StringComparison.Ordinal));

        Assert.Equal(
            [
                $"  Kept as changed: terrain {Space}, cell (0, 0): a plugin that loads after the patch has the last word on it (Later.esp)",
                $"  Kept as changed: navmesh {Key(0xA06)} in {Space}, cell {Key(0x600)}: a plugin that loads after the patch has the last word on it (Later.esp)",
            ],
            kept);
    }

    [Fact]
    public void TheDetailedLogHasALinePerCellWithTheSettingsThatWentBackAndTheWaterLevelItKept()
    {
        var lines = Section(Scene(), Detailed).Lines;

        Assert.Contains($"  Cell {Space}, cell (1, 2): lighting, music; 0 of 214 objects still stand (0.0% by count, 0.0% by size)", lines);
        Assert.Contains(
            $"  Cell {Space}, cell (3, 4): music; water level kept: {WaterObject} lies across the water line between the level before the cleaned mod and the one that stays; "
            + "0 of 214 objects still stand (0.0% by count, 0.0% by size)",
            lines);
        Assert.Contains(
            $"  Kept as changed: cell {Space}, cell (5, 6): the cleaned mod's objects still stand around it; 4 of 10 objects still stand (40.0% by count, 25.0% by size); left to later plugins: music (Later.esp)",
            lines);
    }

    [Fact]
    public void TheDetailedLogHasALinePerTerrainQuarterThatHoldsAChange()
    {
        var lines = Section(Scene(), Detailed).Lines;

        Assert.Contains(
            $"  Terrain {Space}, cell (0, 0), south-west quarter: 2 heights, 1 vertex colour, ground textures; 1 height kept under objects that stay; 0 of 214 objects still stand (0.0% by count, 0.0% by size)",
            lines);
        Assert.Contains(
            $"  Kept as changed: terrain {Space}, cell (0, 0), north-east quarter: the cleaned mod's objects still stand around it; 4 of 10 objects still stand (40.0% by count, 25.0% by size)",
            lines);
        Assert.Equal(2, lines.Count(line => line.Contains("quarter:", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheDetailedLogHasALinePerNavmeshWithWhatBecameOfItAndTheOtherMembersOfItsGroup()
    {
        var lines = Section(Scene(), Detailed).Lines;

        Assert.Contains($"  Navmesh {Key(0xA01)} in {Space}, cell {Key(0x600)}: restored together with 1 other navmesh", lines);
        Assert.Contains($"  Navmesh {Key(0xA02)} in {Space}, cell {Key(0x600)}: brought back together with 1 other navmesh", lines);
        Assert.Contains($"  Navmesh {Key(0xA03)} in {Space}, cell {Key(0x600)}: sunk under the world and cut off", lines);
        Assert.Contains($"  Kept as changed: navmesh {Key(0xA04)} in {Space}, cell {Key(0x600)}: linked to navmeshes that stay ({Key(0xA09)}); 0 of 214 objects still stand (0.0% by count, 0.0% by size)", lines);
        Assert.Contains(
            $"  Kept as changed: navmesh {Key(0xA05)} in {Space}, cell {Key(0x600)}: the cleaned mod's objects still stand around it; 4 of 10 objects still stand (40.0% by count, 25.0% by size)", lines);
    }

    [Theory]
    [InlineData(NavmeshFaultKind.NoLinkBack, "has a link the other navmesh does not answer")]
    [InlineData(NavmeshFaultKind.VertexOutsideBounds, "has a vertex outside its bounds")]
    [InlineData(NavmeshFaultKind.DoorDoesNotStand, "names a door that does not stand")]
    public void ANavmeshDataFaultIsWordedWithTheNavmeshTheTriangleAndTheOtherRecord(NavmeshFaultKind kind, string wording)
    {
        var fault = new NavmeshFault(Key(0xA20), NavmeshRule.Links, kind, Index: 12, Other: Key(0xA21));

        var text = RestoreWhereRemovedText.Refusal(new NavmeshRefusal(NavmeshGuard.DataAsItStandsInconsistent, Key(0xA20), fault), key => key.ToString());

        var place = kind == NavmeshFaultKind.VertexOutsideBounds ? "vertex 12" : "triangle 12";
        Assert.Equal($"inconsistent with navmesh data that stays ({Key(0xA20)} {wording} ({place}, {Key(0xA21)}))", text);
    }

    [Fact]
    public void EveryReasonHasWordsAndNoneUsesTheForbiddenOnes()
    {
        string[] texts =
        [
            .. Enum.GetValues<NeverRestoredReason>().Select(RestoreWhereRemovedText.Never),
            .. Enum.GetValues<AreaVerdict>().Where(verdict => verdict != AreaVerdict.Cleared).Select(RestoreWhereRemovedText.Area),
            .. Enum.GetValues<PlacedGuard>().Select(RestoreWhereRemovedText.Guard),
            .. Enum.GetValues<TerrainKeptReason>().Select(RestoreWhereRemovedText.TerrainKept),
            .. Enum.GetValues<NavmeshGuard>().Select(RestoreWhereRemovedText.Guard),
            .. Enum.GetValues<NavmeshFaultKind>().Select(kind => RestoreWhereRemovedText.Fault(new NavmeshFault(Key(1), NavmeshRule.Identity, kind, Index: null, Other: null))),
            .. Enum.GetValues<CellPart>().Select(RestoreWhereRemovedText.PartOfCell),
            .. Enum.GetValues<NavmeshUndo>().SelectMany(undo => new[] { RestoreWhereRemovedText.Undo(undo), RestoreWhereRemovedText.UndoVerb(undo) }),
            .. Enum.GetValues<PlacedRestoreKind>().SelectMany(kind => new[] { RestoreWhereRemovedText.Kind(kind), RestoreWhereRemovedText.KindVerb(kind) }),
            .. Enumerable.Range(0, 4).Select(RestoreWhereRemovedText.Quarter),
            .. Enum.GetValues<CloneRefusal>().Select(RestoreWhereRemovedText.CloneRefused),
            .. Enum.GetValues<WaterKeptReason>().Select(reason => RestoreWhereRemovedText.WaterKept(reason, Key(1), key => key.ToString())),
            RestoreWhereRemovedText.TexturesKeptForObjectThatStays,
        ];

        Assert.All(texts, text => Assert.False(string.IsNullOrWhiteSpace(text)));
        Assert.All(texts, text => Assert.DoesNotMatch(@"\b(needed|should|hint)\b", text));
        Assert.Throws<UnreachableException>(() => RestoreWhereRemovedText.Area(AreaVerdict.Cleared));
    }

    [Fact]
    public void TheDetailedLogEndsWithTheNumberOfPassesAndTheStepsTimeAndTheTimeOfEachPart()
    {
        var lines = Section(Scene(), Detailed).Lines;

        string[] expected =
        [
            "Restore where removed: 3 passes in 1.5s.",
            "Step time: Restore where removed, areas judged and plans made in 1.2s.",
            "Step time: Restore where removed, world after removal in 2.3s.",
            "Step time: Restore where removed, objects that stay and guards set up, with the first collision layer read in 3.4s.",
            "Step time: Restore where removed, passes in 4.5s.",
            "Step time: Restore where removed, placed guards in the passes in 5.6s.",
        ];
        Assert.Equal(expected, lines.TakeLast(expected.Length));
        Assert.DoesNotContain(Section(Scene(), Normal).Lines, line => line.StartsWith("Restore where removed", StringComparison.Ordinal) || line.StartsWith("Step time: ", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDetailedLogCountsTheObjectsLeftOnChangedTerrainAfterThePassesAndNothingWhenThereAreNone()
    {
        var lines = Section(Scene() with { TerrainFindingsLeft = 2 }, Detailed).Lines;

        const string Counted = "Restore where removed: 2 objects that stay or copies on terrain the changed-terrain check finds changed, where the restore writes nothing of what the check counts.";
        Assert.Equal(Counted, lines[lines.IndexOf("Restore where removed: 3 passes in 1.5s.") + 1]);
        Assert.DoesNotContain(Section(Scene(), Detailed).Lines, line => line.Contains("changed-terrain check finds changed", StringComparison.Ordinal));
    }

    [Fact]
    public void TheNormalLogHasNoLinePerChange()
    {
        var lines = Section(Scene(), Normal).Lines;

        Assert.DoesNotContain(lines, line => line.StartsWith("  ", StringComparison.Ordinal));
    }

    // ---- the closing summary item

    [Fact]
    public void TheClosingItemCountsObjectsTerrainCellsAndNavmeshesThatWentBack()
    {
        var item = Assert.Single(LogSections.BuildRestoreWhereRemovedSummarySection(Scene()).Lines);

        Assert.Equal($"{Heading}: 4 objects, terrain in 1 cell, settings of 2 cells, 3 navmeshes.", item);
    }

    // ---- the report files

    [Fact]
    public void TheObjectsFileHasARowPerJudgedPlacedChangeWithTheAreaAndTheResult()
    {
        var table = ObjectsTable();

        Assert.Equal(Scene().Objects.Length, table.Rows.Length);
        Assert.All(table.Rows, row => Assert.Equal(table.Header.Length, row.Length));
        var moved = Row(table, "formKey", Key(0x901).ToString());
        Assert.Equal(
            ("10", "20", "30", "position, rotation", "rotation (Later.esp)", "214", "0", "8000000", "0", "0", "0", "Cleared", "restored"),
            (Field(table, moved, "oldX"), Field(table, moved, "oldY"), Field(table, moved, "oldZ"), Field(table, moved, "undone"), Field(table, moved, "leftToLaterPlugins"),
                Field(table, moved, "objects"), Field(table, moved, "standing"), Field(table, moved, "size"), Field(table, moved, "standingSize"),
                Field(table, moved, "standingPercentByCount"), Field(table, moved, "standingPercentBySize"), Field(table, moved, "verdict"), Field(table, moved, "result")));
        var standing = Row(table, "formKey", Key(0x907).ToString());
        Assert.Equal(
            ("10", "4", "40", "25", "StillStanding", "kept: the cleaned mod's objects still stand around it"),
            (Field(table, standing, "objects"), Field(table, standing, "standing"), Field(table, standing, "standingPercentByCount"), Field(table, standing, "standingPercentBySize"),
                Field(table, standing, "verdict"), Field(table, standing, "result")));
    }

    [Fact]
    public void TheObjectsFileLeavesTheAreaOfARecordThatNeverGoesBackEmpty()
    {
        var never = Row(ObjectsTable(), "formKey", Key(0x909).ToString());
        var objects = ObjectsTable();

        Assert.Equal(
            ("", "", "", "kept: a plugin that loads after the patch has the last word on it (Later.esp)"),
            (Field(objects, never, "objects"), Field(objects, never, "standing"), Field(objects, never, "verdict"), Field(objects, never, "result")));
    }

    [Fact]
    public void TheCellsFileHasARowPerCellTerrainQuarterThatHoldsAChangeAndNavmeshInThatOrder()
    {
        var table = CellsTable();

        Assert.All(table.Rows, row => Assert.Equal(table.Header.Length, row.Length));
        Assert.Equal(
            ["cell", "cell", "cell", "cell", "quarter", "quarter", "navmesh", "navmesh", "navmesh", "navmesh", "navmesh"],
            table.Rows.Select(row => Field(table, row, "kind")));
    }

    [Fact]
    public void ACellRowNamesItsGridPartsAndWhatALaterPluginChangedAgain()
    {
        var table = CellsTable();

        var row = table.Rows[2];

        Assert.Equal(
            ("5", "6", "music", "music (Later.esp)", "StillStanding", "kept: the cleaned mod's objects still stand around it"),
            (Field(table, row, "gridX"), Field(table, row, "gridY"), Field(table, row, "undone"), Field(table, row, "leftToLaterPlugins"), Field(table, row, "verdict"), Field(table, row, "result")));
    }

    [Fact]
    public void AQuarterRowCountsItsPointsAndTheHeightsKeptPerReason()
    {
        var table = CellsTable();

        var south = table.Rows.Single(row => Field(table, row, "quarter") == "south-west");

        Assert.Equal(
            ("0", "0", "3 heights, 1 vertex colour, ground textures", "restored", "1", "0"),
            (Field(table, south, "gridX"), Field(table, south, "gridY"), Field(table, south, "undone"), Field(table, south, "result"), Field(table, south, "keptUnderObjectThatStays"), Field(table, south, "keptTooSteepToStore")));
        var north = table.Rows.Single(row => Field(table, row, "quarter") == "north-east");
        Assert.Equal("kept: the cleaned mod's objects still stand around it", Field(table, north, "result"));
    }

    /// <summary>The cleaned mod changed only the rest of a land record, whose quarters hold no height, colour or texture to put back.</summary>
    [Theory]
    [InlineData(false, "restored")]
    [InlineData(true, "kept: the cleaned mod's objects still stand around it")]
    public void ALandWhoseOnlyChangeIsTheRestOfTheRecordGetsARowOfItsOwn(bool aQuarterStillStands, string result)
    {
        var scene = Scene() with { Objects = [], Cells = [], Navmeshes = [], NavmeshGroups = [], Lands = [RestOnlyLand(aQuarterStillStands)] };

        var table = Assert.Single(BuildTables(scene, Normal));

        var row = Assert.Single(table.Rows);
        Assert.Equal(
            ("rest", "1", "0", "", "the rest of the record", result),
            (Field(table, row, "kind"), Field(table, row, "gridX"), Field(table, row, "gridY"), Field(table, row, "quarter"), Field(table, row, "undone"), Field(table, row, "result")));
    }

    /// <summary>The same land as above: kept, it has a detailed line and a count of its own; restored, the normal log names no quarter.</summary>
    [Fact]
    public void ALandWhoseOnlyChangeIsTheRestOfTheRecordIsNamedWhenKeptAndCountedWithoutQuartersWhenRestored()
    {
        var kept = Scene() with { Objects = [], Cells = [], Navmeshes = [], NavmeshGroups = [], Lands = [RestOnlyLand(lastQuarterStillStands: true)] };
        var restored = kept with { Lands = [RestOnlyLand(lastQuarterStillStands: false)] };

        var keptLines = Section(kept, Detailed).Lines;

        Assert.Contains($"  Kept as changed: terrain {Space}, cell (1, 0): the rest of the record: the cleaned mod's objects still stand around it", keptLines);
        Assert.Contains("Kept because the cleaned mod's objects still stand around them: the rest of 1 land record.", keptLines);
        Assert.Equal($"{Heading}: terrain of 1 cell.", Section(restored, Normal).Lines[0]);
    }

    /// <summary>
    /// Cell (2, 0): the cleaned mod raised a point inside the south-west quarter and one on its line to the south-east quarter, whose
    /// area still stands. The south-west quarter's heights add up: one restored, one kept for the quarter that stays; the south-east
    /// quarter keeps its point for its own area.
    /// </summary>
    [Fact]
    public void AQuartersHeightsAreRestoredOrKeptForAReasonAndAPointOnTheLineToAQuarterThatStaysSaysSo()
    {
        var scene = Scene() with { Objects = [], Cells = [], Navmeshes = [], NavmeshGroups = [], Lands = [LandNextToAQuarterThatStays()] };

        var lines = Section(scene, Detailed).Lines;
        var table = Assert.Single(BuildTables(scene, Normal));

        Assert.Contains(
            $"  Terrain {Space}, cell (2, 0), south-west quarter: 1 height; 1 height kept on the line to a quarter of the cell whose area is not cleared; 0 of 214 objects still stand (0.0% by count, 0.0% by size)",
            lines);
        Assert.Contains("Left as the cleaned mod changed them: terrain at 1 point (1 on the line to a quarter of the cell whose area is not cleared).", lines);
        var south = Row(table, "quarter", "south-west");
        var east = Row(table, "quarter", "south-east");
        Assert.Equal(("2 heights", "1"), (Field(table, south, "undone"), Field(table, south, "keptNextToQuarterThatStays")));
        Assert.Equal(("1 height", "0", "kept: the cleaned mod's objects still stand around it"), (Field(table, east, "undone"), Field(table, east, "keptNextToQuarterThatStays"), Field(table, east, "result")));
    }

    [Fact]
    public void AClearedQuarterKeepingOnlyVertexColoursSaysTheyLieOnALineSharedWithGroundThatStays()
    {
        var land = LandNextToAQuarterThatStays();
        var coloursOnly = LandOutcomes.Judged(
            land.Change with { UndoPoints = PointSet.Of([]), UndoColours = PointSet.Of([16]) },
            land.Quarters,
            heights: PointSet.Of([]),
            land.Colours,
            land.Textures,
            texturesKept: default,
            rest: false,
            kept: [],
            finalHeights: null,
            CellEdgeHeights.Unknown);
        var scene = Scene() with { Objects = [], Cells = [], Navmeshes = [], NavmeshGroups = [], Lands = [coloursOnly] };

        var table = Assert.Single(BuildTables(scene, Normal));

        Assert.Equal("kept: its vertex colours lie on a line shared with ground that stays", Field(table, Row(table, "quarter", "south-west"), "result"));
    }

    /// <summary>An object whose every changed part a later plugin holds is no candidate for a restore, yet the detailed log counts and lists it.</summary>
    [Fact]
    public void AnObjectWhoseEveryChangedPartALaterPluginHoldsIsCountedAndListedInTheDetailedLogOnly()
    {
        var held = Rock(0x920, PlacedParts.None, later: new Dictionary<PlacedParts, PluginName> { [PlacedParts.Rotation] = Later });
        var world = World() with { ChangedRecords = [held] };
        var empty = ObjectsOnly();

        var detailed = LogSections.BuildRestoreWhereRemovedSection(world, empty, Elapsed, Detailed).Lines;
        var normal = LogSections.BuildRestoreWhereRemovedSection(world, empty, Elapsed, Normal).Lines;

        Assert.Contains("Kept by later plugins: 1 object, 0 cells.", detailed);
        Assert.Contains($"  Kept by later plugins: {RecordLabels.Of(Key(0x920), null)} (base (none)) in {Space}: rotation (Later.esp)", detailed);
        Assert.DoesNotContain(normal, line => line.Contains("Kept by later plugins", StringComparison.Ordinal));
    }

    /// <summary>A guard names the record it found by its editor ID when the world knows it.</summary>
    [Fact]
    public void AGuardNamesTheObjectItFoundByItsEditorId()
    {
        var lantern = TestShapes.Placed(Mutagen.Bethesda.Plugins.ModKey.FromNameAndExtension("Later.esp"), 7, new BaseKey(Key(0x300), BaseLinkKind.PlaceableObject), Vector3.Zero) with { EditorId = "Lantern" };
        var world = World() with { OtherModObjects = [lantern] };
        var refused = ObjectsOnly(Refused(Rock(0x913, PlacedParts.Scale), Cleared, new PlacedRefusal(PlacedGuard.HoldsUpOtherModsObject, lantern.Key, Failure: null)));

        var lines = LogSections.BuildRestoreWhereRemovedSection(world, refused, Elapsed, Detailed).Lines;

        Assert.Contains(lines, line => line.Contains($": holding up an object of a trusted mod that would be left without support (Lantern [{lantern.Key}]);", StringComparison.Ordinal));
    }

    /// <summary>
    /// The follow-up of a rock going back removes an object the cleaned mod added on it and pulls down a lantern of the base game: the log counts
    /// both, the detailed log lists each with the rock, and the follow-up report file has a row for each.
    /// </summary>
    [Fact]
    public void TheFollowUpIsCountedListedAndReported()
    {
        var lantern = TestShapes.Placed(Mutagen.Bethesda.Plugins.ModKey.FromNameAndExtension("Base.esm"), 7, new BaseKey(Key(0x300), BaseLinkKind.PlaceableObject), new Vector3(1, 2, 50)) with { EditorId = "Lantern" };
        var world = World() with { OtherModObjects = [lantern] };
        var rock = Rock(0x913, PlacedParts.Position);
        var result = ObjectsOnly(Restored(rock, Cleared)) with
        {
            FollowUp = new RestoreFollowUp(
                [new FollowUpTarget(new TargetId(0), RestoredTo: null, [rock.Key])],
                [new FollowUpOtherObject(new OtherModsObjectOutcome(lantern, OtherModsObjectFate.PulledDown, LostShare: 1f, Drop: 40f, ReferenceRoles.None), [rock.Key])]),
        };

        var normal = LogSections.BuildRestoreWhereRemovedSection(world, result, Elapsed, Normal).Lines;
        var detailed = LogSections.BuildRestoreWhereRemovedSection(world, result, Elapsed, Detailed with { Bases = TestBaseFacts.Unresolved }).Lines;
        var table = Assert.Single(Tables.BuildRestoredWhereRemovedTables(world, result, [], Normal), file => file.FileName == ReportFileNames.RestoredWhereRemovedFollowUpFileName);

        Assert.Contains("Follow-up of what went back: 1 object the cleaned mod added or moved removed, 1 object of a trusted mod pulled down.", normal);
        var startedBy = $", with {RecordLabels.Of(rock.Key, null)} going back";
        Assert.Contains(detailed, line => line.StartsWith("  Follow-up of what went back: ", StringComparison.Ordinal) && line.EndsWith($": removed{startedBy}", StringComparison.Ordinal));
        Assert.Contains(detailed, line => line.StartsWith($"  Follow-up of what went back: Lantern [{lantern.Key}]", StringComparison.Ordinal) && line.EndsWith($"pulled down by 40.0 units{startedBy}", StringComparison.Ordinal));
        Assert.Equal(["object the cleaned mod added or moved", "trusted mod's object"], table.Rows.Select(row => row[0].Trim('"')));
        Assert.Equal(["removed", "pulled down"], table.Rows.Select(row => row[7].Trim('"')));
        Assert.All(table.Rows, row => Assert.Equal(rock.Key.ToString(), row[9].Trim('"')));
    }

    /// <summary>
    /// The ground going back down pulls down one lantern of the base game alone, and another together with a rock going back: the
    /// detailed log names the ground after the rocks, and the follow-up report file appends "ground" to the keys.
    /// </summary>
    [Fact]
    public void AFollowUpStartedByTheGroundNamesTheGroundInTheLogAndTheReportFile()
    {
        var baseGame = Mutagen.Bethesda.Plugins.ModKey.FromNameAndExtension("Base.esm");
        var alone = TestShapes.Placed(baseGame, 7, new BaseKey(Key(0x300), BaseLinkKind.PlaceableObject), new Vector3(1, 2, 50)) with { EditorId = "Lantern" };
        var withRock = TestShapes.Placed(baseGame, 8, new BaseKey(Key(0x300), BaseLinkKind.PlaceableObject), new Vector3(3, 4, 50)) with { EditorId = "Lamp" };
        var world = World() with { OtherModObjects = [alone, withRock] };
        var rock = Rock(0x913, PlacedParts.Position);
        static OtherModsObjectOutcome Pulled(OtherObject placed) => new(placed, OtherModsObjectFate.PulledDown, LostShare: 1f, Drop: 40f, ReferenceRoles.None);
        var result = ObjectsOnly(Restored(rock, Cleared)) with
        {
            FollowUp = new RestoreFollowUp(
                [],
                [new FollowUpOtherObject(Pulled(alone), []) { ByGround = true }, new FollowUpOtherObject(Pulled(withRock), [rock.Key]) { ByGround = true }]),
        };

        var detailed = LogSections.BuildRestoreWhereRemovedSection(world, result, Elapsed, Detailed with { Bases = TestBaseFacts.Unresolved }).Lines;
        var table = Assert.Single(Tables.BuildRestoredWhereRemovedTables(world, result, [], Normal), file => file.FileName == ReportFileNames.RestoredWhereRemovedFollowUpFileName);

        Assert.Contains(detailed, line => line.StartsWith($"  Follow-up of what went back: Lantern [{alone.Key}]", StringComparison.Ordinal) && line.EndsWith("pulled down by 40.0 units, with the ground going back down", StringComparison.Ordinal));
        Assert.Contains(
            detailed,
            line => line.StartsWith($"  Follow-up of what went back: Lamp [{withRock.Key}]", StringComparison.Ordinal)
                && line.EndsWith($"pulled down by 40.0 units, with {RecordLabels.Of(rock.Key, null)} going back and the ground going back down", StringComparison.Ordinal));
        Assert.Equal(["ground", $"{rock.Key} ground"], table.Rows.Select(row => row[9].Trim('"')));
    }

    /// <summary>
    /// Two rocks go back and would float where they arrive: the follow-up then pulls one down and removes the other. Each keeps its
    /// restored row, which names what the follow-up then did, and the counts of what went back state both.
    /// </summary>
    [Fact]
    public void ARestoredObjectTheFollowUpThenTakesAwayNamesItsFollowUp()
    {
        var world = World();
        var (pulled, removed) = (Rock(0x913, PlacedParts.Position), Rock(0x914, PlacedParts.Position));
        var result = ObjectsOnly(Restored(pulled, Cleared), Restored(removed, Cleared)) with
        {
            FollowUp = new RestoreFollowUp([], [ArrivalFollowedUp(pulled, OtherModsObjectFate.PulledDown), ArrivalFollowedUp(removed, OtherModsObjectFate.Removed)]),
        };

        var normal = LogSections.BuildRestoreWhereRemovedSection(world, result, Elapsed, Normal).Lines;
        var summary = Assert.Single(LogSections.BuildRestoreWhereRemovedSummarySection(result).Lines);
        var detailed = LogSections.BuildRestoreWhereRemovedSection(world, result, Elapsed, Detailed with { Bases = TestBaseFacts.Unresolved }).Lines;
        var table = Assert.Single(Tables.BuildRestoredWhereRemovedTables(world, result, [], Normal), file => file.FileName == ReportFileNames.RestoredWhereRemovedObjectsFileName);

        const string Followed = "1 of them then pulled down by the follow-up of what went back, 1 of them then removed by the follow-up of what went back.";
        Assert.Contains($"{Heading}: 2 objects of other mods (2 moved back), {Followed}", normal);
        Assert.Equal($"{Heading}: 2 objects, {Followed}", summary);
        const string PulledText = "then pulled down to (10, 20, 0) by the follow-up of what went back";
        const string RemovedText = "then removed by the follow-up of what went back";
        Assert.Contains(detailed, line => line.StartsWith($"  Moved back: {RecordLabels.Of(pulled.Key, null)}", StringComparison.Ordinal) && line.Contains($" to (10, 20, 30); {PulledText};", StringComparison.Ordinal));
        Assert.Contains(detailed, line => line.StartsWith($"  Moved back: {RecordLabels.Of(removed.Key, null)}", StringComparison.Ordinal) && line.Contains($" to (10, 20, 30); {RemovedText};", StringComparison.Ordinal));
        Assert.Equal([$"restored; {PulledText}", $"restored; {RemovedText}"], table.Rows.Select(row => Field(table, row, "result")));
    }

    /// <summary>The follow-up of a placed record where it arrives at (10, 20, 30), started by itself; pulled down onto the ground at height 0.</summary>
    private static FollowUpOtherObject ArrivalFollowedUp(RecordChangedByCleanedMod change, OtherModsObjectFate fate)
    {
        var arrived = TestShapes.Placed(Mutagen.Bethesda.Plugins.ModKey.FromNameAndExtension("Base.esm"), 0, new BaseKey(Key(0x300), BaseLinkKind.PlaceableObject), new Vector3(10, 20, 30)) with { Key = change.Key };
        var outcome = new OtherModsObjectOutcome(arrived, fate, LostShare: 1f, Drop: fate == OtherModsObjectFate.PulledDown ? 30f : 0f, ReferenceRoles.None);
        return new FollowUpOtherObject(outcome, [change.Key]);
    }

    /// <summary>A size of a real area passes what a float holds exactly.</summary>
    [Fact]
    public void TheSizeColumnsKeepEveryDigitOfALargeArea()
    {
        var large = Cleared with { Size = 123_456_789d, StandingSize = 12_345_678d };

        var table = Assert.Single(BuildTables(ObjectsOnly(Restored(Rock(0x914, PlacedParts.Position), large)), Normal));

        Assert.Equal(("123456789", "12345678"), (Field(table, table.Rows[0], "size"), Field(table, table.Rows[0], "standingSize")));
    }

    [Fact]
    public void ANavmeshRowNamesItsCellItsPartsAndTheReasonItsGroupStays()
    {
        var table = CellsTable();

        var refused = Row(table, "navmesh", Key(0xA04).ToString());
        var restored = Row(table, "navmesh", Key(0xA01).ToString());

        Assert.Equal(
            (Key(0x600).ToString(), $"kept: linked to navmeshes that stay ({Key(0xA09)})"),
            (Field(table, refused, "cell"), Field(table, refused, "result")));
        Assert.Equal(("shape", "restored"), (Field(table, restored, "undone"), Field(table, restored, "result")));
    }

    [Fact]
    public void BothFilesAreWrittenOnlyWithARowAndNeverOutOfTheListOfKnownNames()
    {
        var onlyCells = Scene() with { Objects = [], Lands = [], Navmeshes = [], NavmeshGroups = [] };

        var tables = BuildTables(onlyCells, Normal);

        Assert.Equal([ReportFileNames.RestoredWhereRemovedCellsFileName], tables.Select(table => table.FileName));
    }

    // ---- copies

    [Fact]
    public void TheNormalLogCountsTheObjectsPutBackWithACopyAndThoseKeptBecauseTheyMayNotBeCopiedPerReason()
    {
        var lines = Section(CopyScene(), Normal).Lines;

        Assert.Equal(
            string.Join('\n',
                $"{Heading}: 1 object of other mods (1 moved back).",
                "Other mods' objects put back, with a copy at the cleaned mod's place: 1.",
                "Other mods' objects kept at the cleaned mod's place because they may not be copied: 3 (1 not plain scenery, 1 persistent, 1 with a check that stopped unexpectedly)."),
            string.Join('\n', lines));
    }

    [Fact]
    public void TheDetailedLogSaysWhichObjectGoesBackWithACopyAndWhyAnotherMayNotBeCopied()
    {
        var lines = Section(CopyScene(), Detailed).Lines;

        Assert.Contains(
            $"  Moved back: {RecordLabels.Of(Key(0x930), null)} (base (none)) in {Space} to (10, 20, 30), with a copy at the cleaned mod's place; 0 of 214 objects still stand (0.0% by count, 0.0% by size)",
            lines);
        Assert.Contains(
            $"  Kept as changed: {RecordLabels.Of(Key(0x931), null)} (base (none)) in {Space}: the cleaned mod's standing content holds it and it may not be copied (persistent); 0 of 214 objects still stand (0.0% by count, 0.0% by size)",
            lines);
        Assert.Contains(lines, line => line.StartsWith($"  Kept as changed: {RecordLabels.Of(Key(0x932), null)} (base (none)) in {Space}: the cleaned mod's objects still stand around it and it may not be copied (not plain scenery); at its old place", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("it may not be copied (on changed terrain as the cleaned mod's object (the check stopped unexpectedly: InvalidOperationException: broke));", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("Kept because the cleaned mod's objects still stand around them", StringComparison.Ordinal));
    }

    [Fact]
    public void TheObjectsFileNamesTheCopyWrittenAndWhyARecordMayNotBeCopied()
    {
        var copy = new RecordKey(Patch, 0x800);

        var table = Assert.Single(BuildTables(CopyScene(), Normal, new CloneWritten(Key(0x930), copy)));

        var copied = Row(table, "formKey", Key(0x930).ToString());
        Assert.Equal(("restored with a copy at the cleaned mod's place", copy.ToString(), ""), (Field(table, copied, "result"), Field(table, copied, "copyFormKey"), Field(table, copied, "copyRefusedBy")));
        var held = Row(table, "formKey", Key(0x931).ToString());
        Assert.Equal(("", "persistent"), (Field(table, held, "copyFormKey"), Field(table, held, "copyRefusedBy")));
        Assert.Equal("not plain scenery", Field(table, Row(table, "formKey", Key(0x932).ToString()), "copyRefusedBy"));
        Assert.All(table.Rows, row => Assert.Equal(table.Header.Length, row.Length));
    }

    /// <summary>A quarter whose heights go back while its textures stay for an object that stays says why the textures stay.</summary>
    [Fact]
    public void AQuarterWhoseHeightsGoBackWhileItsTexturesStayForAnObjectThatStaysSaysWhy()
    {
        var land = Land();
        var texturesKept = LandOutcomes.Judged(
            land.Change, land.Quarters, land.Heights, land.Colours, textures: QuarterSet.Of([]), texturesKept: QuarterSet.Of([0]), rest: false, land.Kept, finalHeights: null, CellEdgeHeights.Unknown);
        var scene = Scene() with { Objects = [], Cells = [], Navmeshes = [], NavmeshGroups = [], Lands = [texturesKept] };

        var line = Assert.Single(Section(scene, Detailed).Lines, text => text.Contains("south-west quarter:", StringComparison.Ordinal));

        Assert.Equal(
            $"  Terrain {Space}, cell (0, 0), south-west quarter: 2 heights, 1 vertex colour; 1 height kept under objects that stay; ground textures kept: "
            + $"{RestoreWhereRemovedText.TexturesKeptForObjectThatStays}; 0 of 214 objects still stand (0.0% by count, 0.0% by size)",
            line);
    }

    [Fact]
    public void TheCellsFileNamesTheTexturesAndTheWaterLevelThatStayInAQuarterOrCellThatGoesBack()
    {
        var land = Land();
        var texturesKept = LandOutcomes.Judged(
            land.Change, land.Quarters, land.Heights, land.Colours, textures: QuarterSet.Of([]), texturesKept: QuarterSet.Of([0]), rest: false, land.Kept, finalHeights: null, CellEdgeHeights.Unknown);
        var scene = Scene() with { Objects = [], Navmeshes = [], NavmeshGroups = [], Lands = [texturesKept] };

        var table = Assert.Single(BuildTables(scene, Normal), file => file.FileName == ReportFileNames.RestoredWhereRemovedCellsFileName);

        Assert.Equal($"restored; ground textures kept: {RestoreWhereRemovedText.TexturesKeptForObjectThatStays}", Field(table, Row(table, "quarter", "south-west"), "result"));
        Assert.Equal(
            $"restored; water level kept: {WaterObject} lies across the water line between the level before the cleaned mod and the one that stays",
            Field(table, Row(table, "gridX", "3"), "result"));
    }

    [Fact]
    public void ACellKeepingItsWaterLevelForTheChangedTerrainCheckOfAnObjectNamesTheObject()
    {
        var cell = Cell(3, 4, [CellPart.Music], Cleared, restored: true, waterKeptBy: WaterObject) with { WaterKeptFor = WaterKeptReason.ForChangedTerrainCheck };
        var scene = Scene() with { Objects = [], Cells = [cell], Lands = [], Navmeshes = [], NavmeshGroups = [] };

        var lines = Section(scene, Detailed).Lines;

        Assert.Contains(
            $"  Cell {Space}, cell (3, 4): music; water level kept: the changed-terrain check of {WaterObject} or of its copy would count the level going back, or stopped there; 0 of 214 objects still stand (0.0% by count, 0.0% by size)",
            lines);
    }

    [Fact]
    public void ACellKeepingItsWaterLevelForAnObjectThatStaysWhereTheCleanedModPutItNamesTheObject()
    {
        var cell = Cell(3, 4, [CellPart.Music], Cleared, restored: true, waterKeptBy: WaterObject) with { WaterKeptFor = WaterKeptReason.ObjectAtWaterLine };
        var scene = Scene() with { Objects = [], Cells = [cell], Lands = [], Navmeshes = [], NavmeshGroups = [] };

        var lines = Section(scene, Detailed).Lines;

        Assert.Contains(
            $"  Cell {Space}, cell (3, 4): music; water level kept: {WaterObject}, which stays where the cleaned mod put it or leaves a copy there, lies across the water line "
            + "between the level before the cleaned mod and the one that stays; 0 of 214 objects still stand (0.0% by count, 0.0% by size)",
            lines);
    }

    // ---- the scene

    private static ImmutableArray<CsvTable> BuildTables(RestoreWhereRemovedResult result, ReportContext context, params CloneWritten[] clones) =>
        Tables.BuildRestoredWhereRemovedTables(World(), result, [.. clones], context);

    /// <summary>
    /// Base game rocks the cleaned mod moved, whose old places are cleared: one goes back with a copy; one its standing content holds
    /// may not be copied, being persistent; two stay for their areas because they may not be copied, one with a check that stopped.
    /// </summary>
    private static RestoreWhereRemovedResult CopyScene() =>
        ObjectsOnly(
            Restored(Rock(0x930, PlacedParts.Position), Cleared) with { Cloning = new CloneJudged(CloneNeed.AreaStillStands, RefusedBy: null) },
            Restored(Rock(0x931, PlacedParts.Position), Cleared) with { IsRestored = false, Cloning = new CloneJudged(CloneNeed.NeighbourOfStandingObject, CloneRefusal.Persistent) },
            KeptForArea(Rock(0x932, PlacedParts.Position), Cleared, AreaVerdict.StillStanding, now: StillStanding) with
            {
                Cloning = new CloneJudged(CloneNeed.AreaStillStands, CloneRefusal.NotPlainScenery),
            },
            KeptForArea(Rock(0x933, PlacedParts.Position), Cleared, AreaVerdict.StillStanding, now: StillStanding) with
            {
                Cloning = new CloneJudged(CloneNeed.AreaStillStands, CloneRefusal.OnChangedTerrainAsCleanedModsObject, "InvalidOperationException: broke"),
            });

    private static LogSection Section(RestoreWhereRemovedResult result, ReportContext context) =>
        LogSections.BuildRestoreWhereRemovedSection(World(), result, Elapsed, context);

    private static CsvTable ObjectsTable() =>
        Assert.Single(BuildTables(Scene(), Normal), file => file.FileName == ReportFileNames.RestoredWhereRemovedObjectsFileName);

    private static CsvTable CellsTable() =>
        Assert.Single(BuildTables(Scene(), Normal), file => file.FileName == ReportFileNames.RestoredWhereRemovedCellsFileName);

    private static ImmutableArray<string> Row(CsvTable table, string column, string value) =>
        table.Rows.Single(row => Field(table, row, column) == value);

    /// <summary>The value of a field, without the quotes a comma in it needed.</summary>
    private static string Field(CsvTable table, ImmutableArray<string> row, string column)
    {
        var field = row[table.Header.IndexOf(column)];
        return field.StartsWith('"') ? field[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal) : field;
    }

    /// <summary>Only the given placed changes judged, none of the other kinds.</summary>
    private static RestoreWhereRemovedResult ObjectsOnly(params PlacedChangeJudged[] objects) =>
        Scene() with { Objects = [.. objects], Cells = [], Lands = [], Navmeshes = [], NavmeshGroups = [] };

    private static CollectedObjects World(int targets = 1) =>
        TestScenes.CreateWorld(TestTargets.CreateMany(targets), []) with
        {
            Spaces = new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, "Tamriel", SpaceKind.Worldspace, "Skyrim") },
        };

    private static RecordKey Key(uint id) => new(BaseGame, id);

    /// <summary>
    /// Four objects that went back (moved back with a rotation left to a later plugin, shown again, hidden again, own model again),
    /// two refused by a guard (one with a check that stopped), one that stays for its area, one with no objects around, one that
    /// never goes back; four cells; one land; and navmeshes in four groups.
    /// </summary>
    private static RestoreWhereRemovedResult Scene() =>
        new(
            StepOutcome.Performed,
            [
                Restored(Rock(0x901, PlacedParts.Position | PlacedParts.Rotation, later: new Dictionary<PlacedParts, PluginName> { [PlacedParts.Rotation] = Later }), Cleared),
                Restored(Rock(0x902, PlacedParts.Presence, removedBefore: false, later: new Dictionary<PlacedParts, PluginName> { [PlacedParts.Scale] = Patch }), Cleared),
                Restored(Rock(0x903, PlacedParts.Presence, removedBefore: true), Cleared),
                Restored(Rock(0x904, PlacedParts.BaseAndData | PlacedParts.Position), Cleared),
                Refused(Rock(0x905, PlacedParts.Position), Cleared, new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, Key(0x500), Failure: null)),
                Refused(Rock(0x906, PlacedParts.Position), Cleared, new PlacedRefusal(PlacedGuard.TooCloseToWhatStays, By: null, "InvalidOperationException: broke")),
                KeptForArea(Rock(0x907, PlacedParts.Position), StillStanding, AreaVerdict.StillStanding),
                KeptForArea(Rock(0x908, PlacedParts.Position), NoObjects, AreaVerdict.NoObjectsAround),
                NeverRestored(Rock(0x909, PlacedParts.Position) with { WinningMod = Later }, NeverRestoredReason.WinnerLoadsAfterPatch),
            ],
            [
                Cell(1, 2, [CellPart.Lighting, CellPart.Music], Cleared, restored: true),
                Cell(3, 4, [CellPart.Music], Cleared, restored: true, waterKeptBy: WaterObject),
                Cell(5, 6, [CellPart.Music], StillStanding, keptForArea: AreaVerdict.StillStanding, later: Later),
                Cell(7, 8, [CellPart.Lighting], area: null, never: NeverRestoredReason.LinkedFromCleanedMod),
            ],
            [Land()],
            [
                Navmesh(0xA01, NavmeshCase.Edited, NavmeshUndo.Geometry, Cleared),
                Navmesh(0xA02, NavmeshCase.Deleted,NavmeshUndo.BroughtBack, Cleared),
                Navmesh(0xA03, NavmeshCase.Added, NavmeshUndo.Sunk, Cleared),
                Navmesh(0xA04, NavmeshCase.Edited, NavmeshUndo.Geometry, Cleared),
                Navmesh(0xA05, NavmeshCase.Edited, undo: null, StillStanding, keptForArea: AreaVerdict.StillStanding),
            ],
            [
                new NavmeshGroupJudged([Key(0xA01), Key(0xA02)], new NavmeshGroupWrite([], [], []), Refused: null),
                new NavmeshGroupJudged([Key(0xA03)], new NavmeshGroupWrite([], [], []), Refused: null),
                new NavmeshGroupJudged([Key(0xA04)], Write: null, new NavmeshRefusal(NavmeshGuard.EdgeLinkUnmatched, Key(0xA09), Fault: null)),
            ],
            Passes: 3,
            DoorsLeftRemoved: [],
            Failures: [],
            new RestoreWhereRemovedTimes(
                PlansMade: TimeSpan.FromSeconds(1.2),
                WorldAfterRemoval: TimeSpan.FromSeconds(2.3),
                GuardsSetUp: TimeSpan.FromSeconds(3.4),
                Passes: TimeSpan.FromSeconds(4.5),
                PlacedGuardsInPasses: TimeSpan.FromSeconds(5.6)));

    private static PlacedChangeJudged Restored(RecordChangedByCleanedMod change, AreaShare area) =>
        new(change, Never: null, area, WhereItIsNow: null, Refused: null, IsRestored: true, ChangeJudge.KindOf(change), KeptForArea: null, PlacedBack: null);

    private static PlacedChangeJudged Refused(RecordChangedByCleanedMod change, AreaShare area, PlacedRefusal refusal) =>
        new(change, Never: null, area, WhereItIsNow: null, refusal, IsRestored: false, ChangeJudge.KindOf(change), KeptForArea: null, PlacedBack: null);

    private static PlacedChangeJudged KeptForArea(RecordChangedByCleanedMod change, AreaShare area, AreaVerdict keeping, AreaShare? now = null) =>
        new(change, Never: null, area, now, Refused: null, IsRestored: false, ChangeJudge.KindOf(change), keeping, PlacedBack: null);

    private static PlacedChangeJudged NeverRestored(RecordChangedByCleanedMod change, NeverRestoredReason never) =>
        new(change, never, OldPlace: null, WhereItIsNow: null, Refused: null, IsRestored: false, ChangeJudge.KindOf(change), KeptForArea: null, PlacedBack: null);

    private static RecordChangedByCleanedMod Rock(uint id, PlacedParts undo, bool removedBefore = false, IReadOnlyDictionary<PlacedParts, PluginName>? later = null)
    {
        var earlier = TestTargets.EarlierVersionAt(BaseGame, new EulerPlacement(new Vector3(10, 20, 30), Vector3.Zero, 1f)) with
        {
            Undo = undo,
            LeftToLaterPlugins = later is null ? ImmutableSortedDictionary<PlacedParts, PluginName>.Empty : later.ToImmutableSortedDictionary(),
        };
        return new RecordChangedByCleanedMod(
            Key(id),
            EditorId: null,
            TestTargets.Space,
            new CellFact(TestTargets.Space, null),
            TestTargets.TargetName,
            WhereItIsNow: null,
            BaseNow: null,
            TestPlacedRecords.PlainReference,
            IsPersistent: false,
            earlier,
            Target: null,
            LinkedFromCleanedMod: false,
            new PlacedPresence(removedBefore, Parent: null, OppositeOfParent: false));
    }

    private static CellChangeJudged Cell(
        int x,
        int y,
        CellPart[] undone,
        AreaShare? area,
        bool restored = false,
        RecordKey? waterKeptBy = null,
        AreaVerdict? keptForArea = null,
        PluginName? later = null,
        NeverRestoredReason? never = null)
    {
        var water = new CellWaterCopy(BaseGame, HasWater: false, OwnLevel: null);
        ImmutableArray<PartChange> parts =
        [
            .. undone.Select(part => new PartChange(new CellPartName(part, null), new PartOutcome.Undo())),
            .. later is { } plugin ? new[] { new PartChange(new CellPartName(CellPart.Music, null), new PartOutcome.LeftToLaterPlugin(plugin)) } : Array.Empty<PartChange>(),
        ];
        var change = new CellChangedByCleanedMod(
            new RecordKey(BaseGame, (uint)(0x500 + x)), TestTargets.Space, (x, y), parts, LinkedFromCleanedMod: false, TestTargets.TargetName, water, water, DefaultWaterLevel: null);
        return new CellChangeJudged(change, [.. parts.Where(part => part.Outcome is PartOutcome.Undo)], never, area, waterKeptBy, restored, keptForArea);
    }

    /// <summary>
    /// Cell (0, 0): the cleaned mod changed three heights and a colour of the south-west quarter and a height of the north-east
    /// quarter, and the textures of both. Two heights go back, one stays under an object that stays, the north-east quarter's area
    /// still stands.
    /// </summary>
    private static LandChangeJudged Land()
    {
        var northEast = 20 * HeightRowLength + 20;
        var cell = new ExteriorCell(TestTargets.Space, 0, 0);
        var change = new LandChangedByCleanedMod(
            cell,
            new RecordKey(BaseGame, 0x650),
            PointSet.Of([0, 1, HeightRowLength, northEast]),
            PointSet.Of([0]),
            QuarterSet.Of([0, 3]),
            UndoRest: false,
            new LandLeftToLater(PointSet.Of([]), PointSet.Of([]), default, Rest: false, []),
            TestTargets.TargetName,
            Heights: null);
        return LandOutcomes.Judged(
            change,
            [new(Cleared), new(Cleared), new(Cleared), new(StillStanding)],
            PointSet.Of([0, HeightRowLength]),
            PointSet.Of([0]),
            QuarterSet.Of([0]),
            texturesKept: default,
            rest: false,
            [new LandPointsKept(TerrainKeptReason.UnderObjectThatStays, PointSet.Of([1]))],
            finalHeights: null,
            CellEdgeHeights.Unknown);
    }

    /// <summary>
    /// Cell (2, 0): the cleaned mod raised the point (5, 5) of the south-west quarter, which goes back, and the point (16, 5) on its line
    /// to the south-east quarter, whose area still stands, which is kept for that.
    /// </summary>
    private static LandChangeJudged LandNextToAQuarterThatStays()
    {
        var inside = 5 * HeightRowLength + 5;
        var onTheLine = 5 * HeightRowLength + 16;
        var change = new LandChangedByCleanedMod(
            new ExteriorCell(TestTargets.Space, 2, 0),
            new RecordKey(BaseGame, 0x652),
            PointSet.Of([inside, onTheLine]),
            PointSet.Of([]),
            QuarterSet.Of([]),
            UndoRest: false,
            new LandLeftToLater(PointSet.Of([]), PointSet.Of([]), default, Rest: false, []),
            TestTargets.TargetName,
            Heights: null);
        return LandOutcomes.Judged(
            change,
            [new(Cleared), new(StillStanding), new(Cleared), new(Cleared)],
            PointSet.Of([inside]),
            PointSet.Of([]),
            QuarterSet.Of([]),
            texturesKept: default,
            rest: false,
            [new LandPointsKept(TerrainKeptReason.NextToQuarterThatStays, PointSet.Of([onTheLine]))],
            finalHeights: null,
            CellEdgeHeights.Unknown);
    }

    /// <summary>Cell (1, 0): the cleaned mod changed only the rest of the record, which goes back unless the last quarter's area still stands.</summary>
    private static LandChangeJudged RestOnlyLand(bool lastQuarterStillStands)
    {
        var change = new LandChangedByCleanedMod(
            new ExteriorCell(TestTargets.Space, 1, 0),
            new RecordKey(BaseGame, 0x651),
            PointSet.Of([]),
            PointSet.Of([]),
            QuarterSet.Of([]),
            UndoRest: true,
            new LandLeftToLater(PointSet.Of([]), PointSet.Of([]), default, Rest: false, []),
            TestTargets.TargetName,
            Heights: null);
        return LandOutcomes.Judged(
            change,
            [new(Cleared), new(Cleared), new(Cleared), new(lastQuarterStillStands ? StillStanding : Cleared)],
            PointSet.Of([]),
            PointSet.Of([]),
            QuarterSet.Of([]),
            texturesKept: default,
            rest: !lastQuarterStillStands,
            [],
            finalHeights: null,
            CellEdgeHeights.Unknown);
    }

    private static NavmeshChangeJudged Navmesh(uint id, NavmeshCase navmeshCase, NavmeshUndo? undo, AreaShare area, AreaVerdict? keptForArea = null)
    {
        var cell = Key(0x600);
        var change = new NavmeshChangedByCleanedMod(
            Key(id),
            TestTargets.Space,
            cell,
            TestTargets.TargetName,
            navmeshCase,
            Geometry: navmeshCase == NavmeshCase.Edited ? new PartOutcome.Undo() : new PartOutcome.Unchanged(),
            Presence: navmeshCase == NavmeshCase.Edited ? new PartOutcome.Unchanged() : new PartOutcome.Undo(),
            MapEntry: new PartOutcome.Unchanged(),
            Earlier: null,
            new NavmeshShape(TestTargets.TargetName, cell, IsDeleted: false, [], [], [], [], MapEntry: null),
            ReplacedBy: [],
            Neighbours: [],
            Doors: [],
            NamedInPreferredPaths: false,
            WinningMapMod: null,
            CellDeletedInAVersion: false,
            LaterWinner: null,
            WinningMapEntry: null);
        return new NavmeshChangeJudged(change, Never: null, area, undo, keptForArea, NothingGoesBack: false);
    }
}
