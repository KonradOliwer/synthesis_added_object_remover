using System.Collections.Immutable;
using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

using static LogSectionScene;

/// <summary>The too-close log section and the too-close lines of the config section give the text checked in as expected-output files or asserted here.</summary>
[ExpectedOutputTest]
public sealed class TooCloseSectionsTests
{
    [Fact]
    public void TooClose_GivesTheTextForTheObjectShapeZoneWithStuckNpcs()
    {
        var report = CreateTooCloseSection(ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInsideTheObject);

        var lines = LogSections.BuildTooCloseSection(report, Detailed).Lines.ToArray();

        AssertExpectedOutput(lines, "TooClose_GivesTheTextForTheObjectShapeZoneWithStuckNpcs");
        Assert.Equal(1, lines.Count(line => line.StartsWith("Object-shape zones:")));
        Assert.Equal(1, lines.Count(line => line.StartsWith("  Sized as a point")));
    }

    [Fact]
    public void TooClose_GivesTheTextForTheBoundingBoxZoneWithIgnoredNpcs()
    {
        var report = CreateTooCloseSection(ZoneShape.BoundingBox, NpcHandling.Never);

        var lines = LogSections.BuildTooCloseSection(report, Detailed).Lines.ToArray();

        AssertExpectedOutput(lines, "TooClose_GivesTheTextForTheBoundingBoxZoneWithIgnoredNpcs");
        Assert.Contains(lines, line => line.StartsWith("NPCs and creatures: ignored;"));
    }

    [Fact]
    public void TooClose_PutsTheShapeZoneStatisticsBeforeTheSummary()
    {
        var lines = LogSections.BuildTooCloseSection(CreateTooCloseSection(ZoneShape.ObjectShape, NpcHandling.AlwaysLikeAnyObject), Detailed).Lines;

        var order = new[] { "Object-shape zones:", "Found " }
            .Select(prefix => lines.ToList().FindIndex(line => line.StartsWith(prefix)))
            .ToArray();
        Assert.Equal(order.Order(), order);
        Assert.DoesNotContain(-1, order);
    }

    /// <summary>The zone and NPC lines follow what the step reports it used, for every pair.</summary>
    [Theory]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.AlwaysLikeAnyObject, false, false, false)]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.OnlyWhenStuckInsideTheObject, false, true, false)]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.OnlyWhenStuckInsideTheObjectWithCollision, false, true, false)]
    [InlineData(ZoneShape.BoundingBox, NpcHandling.Never, false, false, true)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.AlwaysLikeAnyObject, true, false, false)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInsideTheObject, true, true, false)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInsideTheObjectWithCollision, true, true, false)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.Never, true, false, true)]
    public void TooClose_PrintsTheZoneAndNpcLinesTheResultSays(ZoneShape zone, NpcHandling npcs, bool zoneLines, bool stuckLines, bool ignoredLine)
    {
        var lines = LogSections.BuildTooCloseSection(CreateTooCloseSection(zone, npcs), Detailed).Lines.ToArray();

        Assert.Equal(zoneLines, lines.Any(line => line.StartsWith("Object-shape zones:")));
        Assert.Equal(stuckLines, lines.Any(line => line.StartsWith("NPCs and creatures (only when stuck")));
        Assert.Equal(ignoredLine, lines.Any(line => line.StartsWith("NPCs and creatures: ignored;")));
    }

    [Fact]
    public void TooClose_CountsTheStuckNpcObjectsWithoutBlockingCollisionOnlyWhenCollisionIsRequired()
    {
        const string NotCounted = "4 objects with an NPC stuck in them, 1 of them not counted because their collision does not stop characters.";
        var withCollision = LogSections.BuildTooCloseSection(
            CreateTooCloseSection(ZoneShape.BoundingBox, NpcHandling.OnlyWhenStuckInsideTheObjectWithCollision), Detailed).Lines;
        var withoutCollision = LogSections.BuildTooCloseSection(
            CreateTooCloseSection(ZoneShape.BoundingBox, NpcHandling.OnlyWhenStuckInsideTheObject), Detailed).Lines;

        var line = Assert.Single(withCollision, line => line.StartsWith("NPCs and creatures (only when stuck in the object and the object's collision stops characters):"));
        Assert.EndsWith(NotCounted, line);
        Assert.DoesNotContain(withoutCollision, line => line.Contains("not counted"));
    }

    [Fact]
    public void TooClose_ThrowsWhenTheStepHandledNpcsOnlyWhenStuckButGaveNoSummary()
    {
        var report = CreateTooCloseSection(ZoneShape.BoundingBox, NpcHandling.AlwaysLikeAnyObject);
        var inconsistent = report with { Passes = [report.Passes[0] with { Pass = report.Passes[0].Pass with { Npcs = NpcHandling.OnlyWhenStuckInsideTheObject } }] };

        Assert.Throws<InvalidOperationException>(() => LogSections.BuildTooCloseSection(inconsistent, Detailed));
    }

    [Fact]
    public void TooClose_PrintsNothingWhenTheStepIsSwitchedOff()
    {
        Assert.Empty(LogSections.BuildSwitchedOffTooCloseSection().Lines);
    }

    [Theory]
    [InlineData(true, "Margin by object size: on, 0.5 x object size;")]
    [InlineData(false, "Margin by object size: off;")]
    public void Config_SaysWhetherTheSizeMarginIsOn(bool bySize, string expectedPart)
    {
        var lines = ConfigLines(settings => settings.TooClose.BySize.Use = bySize);

        Assert.Contains(expectedPart, TooCloseConfigLine(lines));
    }

    [Fact]
    public void Settings_TheSizeMarginIsOnByDefaultAndPassesThrough()
    {
        Assert.True(new TooCloseRemovalSettings().BySize.Use);
        var settings = SettingsFor();
        settings.TooClose.BySize.Use = false;
        settings.TooClose.ByDistance.Use = true;

        var ready = Assert.IsType<RunRequest.Ready>(Build(settings, Facts()));

        Assert.False(ready.Settings.Collision.TooClose.BySize);
        Assert.Empty(ready.Warnings);
    }

    [Fact]
    public void TooClose_FoundLineNamesTheDistanceWhenOnlyTheDistanceZoneIsOn()
    {
        var report = CreateTooCloseSection(ZoneShape.BoundingBox, NpcHandling.Never, [WithinDistance], WithinDistance);

        var lines = LogSections.BuildTooCloseSection(report, Normal).Lines;

        Assert.Equal("Found 1 of 3 Target.esp objects within 32 units of other mods' objects.", lines[0]);
    }

    [Fact]
    public void TooClose_FoundLineSaysHowManyWereAlreadyRemovedOnChangedTerrain()
    {
        var report = CreateTooCloseSection(ZoneShape.BoundingBox, NpcHandling.Never);
        var removedBefore = report with { Passes = [report.Passes[0] with { AlreadyRemoved = 1 }] };

        var lines = LogSections.BuildTooCloseSection(removedBefore, Normal).Lines;

        Assert.Equal("Found 1 of 3 Target.esp objects too close to other mods' objects; 1 of them were already removed on changed terrain.", lines[0]);
    }

    [Fact]
    public void TooClose_PrintsOneFoundLineForEachSettingsSet()
    {
        var report = CreateTooCloseSection(ZoneShape.BoundingBox, NpcHandling.Never, [BySize], BySize);
        var distance = CreateTooCloseSection(ZoneShape.BoundingBox, NpcHandling.Never, [WithinDistance], WithinDistance, WithinDistance).Passes[0].Pass with
        {
            Objects = [new TargetId(2)],
        };
        var bySize = report.Passes[0].Pass with { Objects = [new TargetId(0), new TargetId(1)] };
        var twoSets = report with { Passes = [new TooClosePassReport(bySize, 0, 0), new TooClosePassReport(distance, 0, 0)] };

        var lines = LogSections.BuildTooCloseSection(twoSets, Normal).Lines.ToArray();

        Assert.Equal(
            [
                "Found 1 of 2 Target.esp objects too close to other mods' objects.",
                "Found 2 of 1 Target.esp objects within 32 units of other mods' objects.",
                $"  Kept {RecordNames.Describe(report.World.Targets[2])} in {Describe.Space(report.World, TestTargets.Space)}: {TestKeepReasons.QuestDetail}.",
            ],
            lines);
    }

    [Fact]
    public void TooClose_ASettingsSetOfObjectsOfOtherModsNamesThemWithoutAnOwnPartAndCountsTheRestoredOnes()
    {
        var report = CreateTooCloseSection(ZoneShape.BoundingBox, NpcHandling.Never, [BySize], BySize);
        var world = report.World with { Targets = [.. report.World.Targets.Select(target => target with { Origin = target.Id.Index == 2 ? TargetOrigin.Moved : TargetOrigin.CleanedMod })] };
        var own = report.Passes[0].Pass with { Objects = [new TargetId(0), new TargetId(1)] };
        var changed = report.Passes[0].Pass with { Objects = [new TargetId(2)] };
        var twoSets = report with { World = world, Passes = [new TooClosePassReport(own, 0, 0), new TooClosePassReport(changed, 0, 1)] };

        var lines = LogSections.BuildTooCloseSection(twoSets, Normal).Lines.Where(line => line.StartsWith("Found ")).ToArray();

        Assert.Equal(
            [
                "Found 1 of 2 Target.esp objects too close to other mods' objects.",
                "Found 1 of 1 objects of other mods that Target.esp moved too close to other mods' objects; 1 of them restored.",
            ],
            lines);
    }

    [Fact]
    public void TooClose_ASettingsSetOfMixedOriginsKeepsTheMixedWording()
    {
        var report = CreateTooCloseSection(ZoneShape.BoundingBox, NpcHandling.Never, [BySize], BySize);
        var world = report.World with { Targets = [.. report.World.Targets.Select(target => target with { Origin = target.Id.Index == 2 ? TargetOrigin.Replaced : TargetOrigin.CleanedMod })] };

        var line = LogSections.BuildTooCloseSection(report with { World = world }, Normal).Lines[0];

        Assert.Equal(
            "Found 1 of 3 objects (2 Target.esp objects and 1 objects of other mods that Target.esp gave another model) too close to other mods' objects.",
            line);
    }

    [Fact]
    public void TooClose_PrintsTheStepsTimeAfterTheLastFoundLineOnly()
    {
        var report = CreateTooCloseSection(ZoneShape.BoundingBox, NpcHandling.Never);
        var twoSets = report with { Passes = [report.Passes[0], report.Passes[0]] };

        var found = LogSections.BuildTooCloseSection(twoSets, Detailed).Lines.Where(line => line.StartsWith("Found ")).ToArray();

        Assert.Equal(2, found.Length);
        Assert.DoesNotContain("not counting the shrink or turn check", found[0]);
        Assert.Contains("not counting the shrink or turn check", found[1]);
    }

    [Fact]
    public void TooClose_FoundLineCountsEachZoneWhenBothZonesAreOn()
    {
        var report = CreateTooCloseSection(
            ZoneShape.BoundingBox, NpcHandling.Never, [BySize, WithinDistance], BySize, WithinDistance, WithinDistance);

        var lines = LogSections.BuildTooCloseSection(report, Normal).Lines;

        Assert.Equal("Found 3 of 3 Target.esp objects too close to other mods' objects (1 by object size, 2 by distance).", lines[0]);
    }

    [Fact]
    public void Config_ShowsTheFixedDistanceOnlyWhenTheDistanceMarginIsOn()
    {
        Assert.Contains("Margin by fixed distance: off;", TooCloseConfigLine(ConfigLines(byDistance: false)));
        Assert.Contains("Margin by fixed distance: on, 50 units;", TooCloseConfigLine(ConfigLines(byDistance: true)));
    }

    [Theory]
    [InlineData(false, "Use rough boxes instead of exact shapes (faster): off;")]
    [InlineData(true, "Use rough boxes instead of exact shapes (faster): on;")]
    public void Config_SaysWhetherRoughBoxesAreUsed(bool roughBoxes, string expectedPart)
    {
        var lines = ConfigLines(settings => settings.TooClose.UseRoughBoxes = roughBoxes);

        Assert.Contains(expectedPart, TooCloseConfigLine(lines));
    }

    private static string TooCloseConfigLine(ImmutableArray<string> lines) =>
        Assert.Single(lines, line => line.StartsWith("Remove objects too close to other mods' objects:"));

    [Fact]
    public void Settings_TheDistanceZoneIsOnByDefaultAndPassesThroughClamped()
    {
        var defaults = new TooCloseRemovalSettings();
        Assert.True(defaults.ByDistance.Use);
        Assert.Equal(50f, defaults.ByDistance.DistanceInUnits);
        var settings = SettingsFor();
        settings.TooClose.ByDistance.Use = true;
        settings.TooClose.ByDistance.DistanceInUnits = 5000f;

        var ready = Assert.IsType<RunRequest.Ready>(Build(settings, Facts()));

        Assert.True(ready.Settings.Collision.TooClose.ByDistance);
        Assert.Equal(1024f, ready.Settings.Collision.TooClose.DistanceInUnits);
        Assert.Single(ready.Warnings);
    }

    [Fact]
    public void TooClose_ForTheNormalLogKeepsTheResultAndTheKeptObjectsOnly()
    {
        var report = CreateTooCloseSection(ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInsideTheObject);

        var lines = LogSections.BuildTooCloseSection(report, Normal).Lines.ToArray();

        Assert.Equal(
            [
                "Found 1 of 3 Target.esp objects too close to other mods' objects.",
                $"  Kept {RecordNames.Describe(report.World.Targets[2])} in {Describe.Space(report.World, TestTargets.Space)}: {TestKeepReasons.QuestDetail}.",
            ],
            lines);
    }
}
