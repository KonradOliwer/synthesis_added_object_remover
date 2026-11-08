using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

using static RestoreWhereRemovedReportScene;

/// <summary>The normal log and the closing summary item of the step that restores what the cleaned mod changed where it was removed, over results built by hand.</summary>
public sealed class RestoreWhereRemovedNormalLogTests
{
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

    [Fact]
    public void TheNormalLogHasNoLinePerChange()
    {
        var lines = Section(Scene(), Normal).Lines;

        Assert.DoesNotContain(lines, line => line.StartsWith("  ", StringComparison.Ordinal));
    }

    [Fact]
    public void TheClosingItemCountsObjectsTerrainCellsAndNavmeshesThatWentBack()
    {
        var item = Assert.Single(LogSections.BuildRestoreWhereRemovedSummarySection(Scene()).Lines);

        Assert.Equal($"{Heading}: 4 objects, terrain in 1 cell, settings of 2 cells, 3 navmeshes.", item);
    }

    [Fact]
    public void TheNormalLogCountsTheObjectsPutBackWithACopyAndThosePutBackWithoutOneBecauseTheyMayNotBeCopiedPerReason()
    {
        var lines = Section(CopyScene(), Normal).Lines;

        Assert.Equal(
            string.Join('\n',
                $"{Heading}: 4 objects of other mods (4 moved back).",
                "Other mods' objects put back, with a copy at the cleaned mod's place: 1.",
                "Other mods' objects put back with no copy at the cleaned mod's place because they may not be copied: 3 (1 not plain scenery, 1 persistent, 1 with a check that stopped unexpectedly)."),
            string.Join('\n', lines));
    }
}
