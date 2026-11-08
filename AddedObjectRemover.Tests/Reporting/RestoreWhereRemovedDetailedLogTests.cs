using System.Diagnostics;
using System.Numerics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

using static RestoreWhereRemovedReportScene;

/// <summary>The detailed log of the step that restores what the cleaned mod changed where it was removed: the reasons in plain words and the lines per change, over results built by hand.</summary>
public sealed class RestoreWhereRemovedDetailedLogTests
{
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
            "Step time: Restore where removed, plans read in the passes in 0.7s.",
            "Step time: Restore where removed, follow-up of what went back in the passes in 0.1s.",
            "Step time: Restore where removed, objects that stand read in the passes in 0.2s.",
            "Step time: Restore where removed, placed guards in the passes in 5.6s.",
            "Step time: Restore where removed, changed-terrain check in the passes in 0.3s.",
            "Step time: Restore where removed, navmesh guards in the passes in 0.4s.",
            "Step time: Restore where removed, ground protected in the passes in 0.5s.",
            "Step time: Restore where removed, water levels kept in the passes in 0.6s.",
        ];
        Assert.Equal(expected, lines.TakeLast(expected.Length));
        Assert.DoesNotContain(Section(Scene(), Normal).Lines, line => line.StartsWith("Restore where removed", StringComparison.Ordinal) || line.StartsWith("Step time: ", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDetailedLogNamesTheRoundsOnlyWhenThereWereMoreThanOne()
    {
        Assert.Contains("Restore where removed: 2 rounds, 3 passes in 1.5s.", Section(Scene() with { Rounds = 2 }, Detailed).Lines);
        Assert.Contains("Restore where removed: 3 passes in 1.5s.", Section(Scene() with { Rounds = 1 }, Detailed).Lines);
    }

    [Fact]
    public void TheDetailedLogNamesEachPartKeptAsAnEarlierRoundLeftItWithWhatItsGoingBackWouldUndo()
    {
        var kept = Scene() with
        {
            KeptForEarlierRound =
            [
                new KeptForEarlierRound(Key(0xA05), Quarter: null, new EarlierRoundHold(Key(0xB99), CauseNamed: true)),
                new KeptForEarlierRound(Key(0xA06), Quarter: null, new EarlierRoundHold(Key(0xB99), CauseNamed: false)),
            ],
        };

        var lines = Section(kept, Detailed).Lines.Where(line => line.StartsWith("  Kept as an earlier round of the restore left it: ", StringComparison.Ordinal)).ToList();
        var undone = RecordLabels.Of(Key(0xB99), editorId: null);
        Assert.Equal(2, lines.Count);
        Assert.EndsWith($"; going back would undo that round's outcome for {undone}.", lines[0], StringComparison.Ordinal);
        Assert.EndsWith($"; going back with every change only this round lets go back would undo that round's outcome for {undone}.", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain(Section(Scene(), Detailed).Lines, line => line.StartsWith("  Kept as an earlier round", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDetailedLogCountsTheObjectsLeftOnChangedTerrainAfterThePassesAndNothingWhenThereAreNone()
    {
        var lines = Section(Scene() with { TerrainFindingsLeft = 2 }, Detailed).Lines;

        const string Counted = "Restore where removed: 2 objects that stay or copies on terrain the changed-terrain check finds changed, where the restore writes nothing of what the check counts.";
        Assert.Equal(Counted, lines[lines.IndexOf("Restore where removed: 3 passes in 1.5s.") + 1]);
        Assert.DoesNotContain(Section(Scene(), Detailed).Lines, line => line.Contains("changed-terrain check finds changed", StringComparison.Ordinal));
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

    [Fact]
    public void TheDetailedLogSaysWhichObjectGoesBackWithACopyAndWhyAnotherMayNotBeCopied()
    {
        var lines = Section(CopyScene(), Detailed).Lines;

        Assert.Contains(
            $"  Moved back: {RecordLabels.Of(Key(0x930), null)} (base (none)) in {Space} to (10, 20, 30), with a copy at the cleaned mod's place; 0 of 214 objects still stand (0.0% by count, 0.0% by size)",
            lines);
        Assert.Contains(
            $"  Moved back: {RecordLabels.Of(Key(0x931), null)} (base (none)) in {Space} to (10, 20, 30), no copy: persistent; 0 of 214 objects still stand (0.0% by count, 0.0% by size)",
            lines);
        Assert.Contains(lines, line => line.StartsWith($"  Moved back: {RecordLabels.Of(Key(0x932), null)} (base (none)) in {Space} to (10, 20, 30), no copy: not plain scenery; 0 of 214 objects",StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("no copy: on changed terrain as the cleaned mod's object (the check stopped unexpectedly: InvalidOperationException: broke);", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("Kept because the cleaned mod's objects still stand around them", StringComparison.Ordinal));
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
}
