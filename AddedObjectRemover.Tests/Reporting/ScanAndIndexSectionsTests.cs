using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Reporting;

using static LogSectionScene;

/// <summary>The scan, thread, index, visibility, group and replacement log sections give the text checked in as expected-output files or asserted here.</summary>
[ExpectedOutputTest]
public sealed class ScanAndIndexSectionsTests
{
    [Fact]
    public void Scan_GivesTheTextWithTheDetailedLog()
    {
        var world = CreateWorld();

        var lines = LogSections.BuildScanSection(world, Target.ToPluginName(), Elapsed, Detailed).Lines.ToArray();

        AssertExpectedOutput(lines, "Scan_GivesTheTextWithTheDetailedLog");
        Assert.Contains("Scanned 9 placed records in 1.5s: 3 Target.esp objects to check, 2 objects from other mods.", lines);
    }

    [Fact]
    public void Scan_ForTheNormalLogDropsTheTimingAndTheInternalCounts()
    {
        var lines = LogSections.BuildScanSection(CreateWorld(), Target.ToPluginName(), Elapsed, Normal).Lines.ToArray();

        Assert.Equal(
            [
                "Scanned 9 placed records: 3 Target.esp objects to check, 2 objects from other mods.",
                "  Ignored 4 Target.esp objects whose winning version comes from a later plugin.",
                "  Ignored 3 Target.esp objects that a later plugin removes.",
                "  Ignored 5 Target.esp objects that are deleted, start disabled, or have no valid position or rotation.",
                "  Ignored 7 other-mod objects that Target.esp itself overrides.",
            ],
            lines);
    }

    [Fact]
    public void Scan_CountsTheCopiesAnEarlierRunPlacedForEachKindOfCleanedMod()
    {
        var world = CreateWorld();
        world = world with { Counts = world.Counts with { ClonesOfCleanedMod = 2, ClonesOfOtherCleanedMods = 1 } };

        var lines = LogSections.BuildScanSection(world, Target.ToPluginName(), Elapsed, Normal).Lines;

        Assert.Contains("  Copies an earlier run placed as Target.esp content: 2; they are judged as Target.esp objects.", lines);
        Assert.Contains("  Copies an earlier run placed as the content of other cleaned mods: 1; they only support or hold other objects.", lines);
        Assert.DoesNotContain(LogSections.BuildScanSection(CreateWorld(), Target.ToPluginName(), Elapsed, Normal).Lines, line => line.Contains("Copies", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_WarnsOfEachRecordOfAPatchWithABrokenCopyMarker()
    {
        var broken = new RecordKey(new PluginName("Patch.esp"), 0x812);
        var world = CreateWorld() with { BrokenCloneMarkers = [broken] };

        var lines = LogSections.BuildScanSection(world, Target.ToPluginName(), Elapsed, Normal).Lines;

        Assert.Equal(
            $"Warning: {broken} in an Added Object Remover patch has an EditorID that starts like the marker of a copy but is no valid marker for the "
            + "plugins in the load order. It is read as no copy and no object of any cleaned mod: it only holds or supports other objects and causes no removal.",
            lines[^1]);
    }

    [Fact]
    public void Threads_ArePrintedOnlyInTheDetailedLog()
    {
        var execution = new Execution(Workers);

        Assert.Equal(["Using 4 threads."], LogSections.BuildThreadsSection(execution, Detailed).Lines.ToArray());
        Assert.Empty(LogSections.BuildThreadsSection(execution, Normal).Lines);
    }

    [Fact]
    public void Index_GivesTheTextWithTheDetailedLogAndNothingWithout()
    {
        var world = CreateWorld();

        AssertExpectedOutput(LogSections.BuildIndexSection(world, Elapsed, Detailed).Lines.ToArray(), "Index_GivesTheTextWithTheDetailedLogAndNothingWithout");
        Assert.Empty(LogSections.BuildIndexSection(world, Elapsed, Normal).Lines);
    }

    [Fact]
    public void TargetVisibilityAndGroups_GiveTheTextWithTheDetailedLogAndNothingWithout()
    {
        ObjectVisibility[] looks = [ObjectVisibility.Visible, ObjectVisibility.Invisible(InvisibleObjectKind.DoorMarkers), ObjectVisibility.MissingBase];
        var targets = looks
            .Select((look, index) => TestTargets.Create(index, TestTargets.At(default), TestVisibility.TaggedBase(look), TestTargets.Space))
            .ToList();
        var shapes = TestVisibility.OfTaggedBasesOnly();
        var groups = LinkedGroups.Build(3, [TestTargets.Link(0, 1)]);

        AssertExpectedOutput(
            [.. LogSections.BuildTargetVisibilitySection(targets, 5, Target.ToPluginName(), Detailed with { Shapes = shapes }).Lines, .. LogSections.BuildGroupsSection(groups, 2, Target.ToPluginName(), Detailed).Lines],
            "TargetVisibilityAndGroups_GiveTheTextWithTheDetailedLogAndNothingWithout");
        Assert.Empty(LogSections.BuildTargetVisibilitySection(targets, 5, Target.ToPluginName(), Normal).Lines);
        Assert.Empty(LogSections.BuildGroupsSection(groups, 2, Target.ToPluginName(), Normal).Lines);
    }

    [Fact]
    public void Replacements_PutTheListThenTheSummary()
    {
        var world = CreateWorld();
        var replacements = Replacements.Of(world.OtherModObjects.Length, [new Replacement(new OtherId(0), new TargetId(1), 3.5f, 0.9f)]);

        AssertExpectedOutput(LogSections.BuildReplacementsSection(world, replacements, Elapsed, Detailed).Lines.ToArray(), "Replacements_PutTheListThenTheSummary");
        Assert.Equal(
            ["Replacement matching: 1 other-mod objects excluded."],
            LogSections.BuildReplacementsSection(world, replacements, Elapsed, Normal).Lines.ToArray());
    }
}
