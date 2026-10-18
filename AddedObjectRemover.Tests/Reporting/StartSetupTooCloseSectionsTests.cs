using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using AddedObjectRemover.Tests.EndToEnd;
using Noggog;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>
/// The start, setup and too-close log sections give the text checked in as expected-output files with the detailed log on,
/// and only what a mod user needs, without timings, with it off.
/// </summary>
public sealed class StartSetupTooCloseSectionsTests
{
    private const string ReportsFolder = "/out/Reports";
    private const int Workers = 4;

    private static readonly TimeSpan Elapsed = TimeSpan.FromSeconds(1.5);
    private static readonly ModKey Target = TestTargets.TargetMod;
    private static readonly ModKey Other = ModKey.FromNameAndExtension("Other.esp");
    private static readonly ModKey Compat = ModKey.FromNameAndExtension("Compat.esp");
    private static readonly ModKey Merged = ModKey.FromNameAndExtension("Merged.esp");
    private static readonly ModKey ExtraA = ModKey.FromNameAndExtension("ExtraA.esp");
    private static readonly ModKey ExtraB = ModKey.FromNameAndExtension("ExtraB.esp");
    private static readonly ModKey Skyrim = ModKey.FromNameAndExtension("Skyrim.esm");
    private static readonly ModKey Patch = ModKey.FromNameAndExtension("Patch.esp");

    private static readonly ReportContext Normal = new(null!, null!, Detailed: false);
    private static readonly ReportContext Detailed = new(null!, null!, Detailed: true);
    private static readonly TooCloseCause BySize = new TooCloseCause.ObjectSize();
    private static readonly TooCloseCause WithinDistance = new TooCloseCause.Distance(32f);

    [Theory]
    [InlineData(null)]
    [InlineData("Missing.esp")]
    public void Stop_GivesTheTextOfEveryUnusableTarget(string? target)
    {
        var settings = new StandardSettings { TargetPlugin = target == null ? ModKey.Null : ModKey.FromNameAndExtension(target) };
        var stop = Assert.IsType<RunRequest.Stop>(Build(settings, Facts()));

        AssertExpectedOutput(LogSections.BuildStopSection(stop.Reason, stop.Source).Lines.ToArray(), "Stop_GivesTheTextOfEveryUnusableTarget", $"{target ?? "null"}");
    }

    [Fact]
    public void Stop_SaysThereIsNothingToCheck()
    {
        Assert.Equal(
            ["Nothing to check. No changes made."],
            LogSections.BuildStopSection(new StopReason(StopKind.NoTargetObjects, null), new SettingsSource.Standard()).Lines.ToArray());
    }

    [Fact]
    public void Stop_SaysTheBatchGoesOnWhenAConfigHasNothingToCheck()
    {
        var config = new SettingsSource.ConfigFile("town", "town.json", "town.json", 1, 2, new ConfigFileNotes([], [], []));

        Assert.Equal(
            ["Nothing to check for this config. No changes made for it; the batch goes on."],
            LogSections.BuildStopSection(new StopReason(StopKind.NoTargetObjects, null), config).Lines.ToArray());
    }

    [Fact]
    public void Stop_SaysTheTargetIsThePatch()
    {
        Assert.Equal(
            ["Target plugin Patch.esp is the patch this patcher writes. No changes made."],
            LogSections.BuildStopSection(new StopReason(StopKind.TargetIsThePatch, "Patch.esp"), new SettingsSource.Standard()).Lines.ToArray());
    }

    [Fact]
    public void Warnings_GiveTheTextInTheOrderTheOptionsBuilderFoundThem()
    {
        var settings = SettingsFor();
        settings.TooClose.NpcHandling = (NpcHandling)99;
        settings.TooClose.BySize.SizeMultiplier = 9f;
        settings.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings { SurroundingObjects = new SurroundingObjectsSettings { SearchRadius = 1f } };
        var ready = Assert.IsType<RunRequest.Ready>(Build(settings, Facts()));

        var lines = LogSections.BuildWarningsSection(ready.Warnings).Lines.ToArray();

        Assert.Equal(ready.Warnings.Select(warning => warning.Message), lines);
        AssertExpectedOutput(lines, "Warnings_GiveTheTextInTheOrderTheOptionsBuilderFoundThem");
    }

    [Fact]
    public void ReportFolderWarning_WordsTheFailedDeletion()
    {
        var section = LogSections.BuildReportFolderWarningSection(
            new ReportFileProblem(ReportFileAction.DeletingEarlierFiles, "/r", FileName: null, "denied"));

        Assert.Equal(["  Warning: deleting earlier diagnostics files in /r failed: denied"], section.Lines.ToArray());
    }

    [Fact]
    public void ReportFileWarnings_WordEachFailedWriteInOrder()
    {
        var section = LogSections.BuildReportFileWarningsSection(
        [
            new ReportFileProblem(ReportFileAction.WritingFile, "/r", "a.csv", "denied"),
            new ReportFileProblem(ReportFileAction.WritingFile, "/r", "b.csv", "in use"),
        ]);

        Assert.Equal(
            ["  Warning: writing a.csv in /r failed: denied", "  Warning: writing b.csv in /r failed: in use"],
            section.Lines.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Config_GivesTheTextWithAndWithoutPatchesAndLeftovers(bool everythingOn)
    {
        var settings = SettingsFor();
        settings.WhatToIgnore = new IgnoreSettings
        {
            IgnoreTargetMasters = everythingOn,
            CompatibilityPatches = new CompatibilityPatchSettings { IgnoreModsPatchedWithTarget = everythingOn, MaxOtherMastersForPatch = 1 },
            ExcludedPlugins = everythingOn ? [ModKey.FromNameAndExtension("Extra.esp"), ModKey.FromNameAndExtension("Second.esp")] : [],
        };
        settings.FollowUpRemoval = new FollowUpRemovalSettings
        {
            Mode = everythingOn ? FollowUpRemovalMode.OnlyWhatLosesItsSupport : FollowUpRemovalMode.Nothing,
        };
        settings.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings
        {
            RemoveLeftoverInvisibleObjects = everythingOn,
            MoveKeptMarkersOutOfOtherModsObjects = everythingOn,
        };
        var mods = Facts();
        var runSettings = Assert.IsType<RunRequest.Ready>(Build(settings, mods, detailedLogAndReports: everythingOn)).Settings;
        var standing = ModIdentifier.Identify(mods, runSettings.ModIdentification);

        var lines = LogSections.BuildConfigSection(runSettings, standing).Lines.Select(line => line.Replace('\\', '/')).ToArray();

        AssertExpectedOutput(lines, "Config_GivesTheTextWithAndWithoutPatchesAndLeftovers", $"{everythingOn}");
    }

    [Fact]
    public void Config_ListsSkippedPluginsAndDetectedPatchesWhenDetectionIsOn()
    {
        var settings = SettingsFor();
        settings.WhatToIgnore = new IgnoreSettings { CompatibilityPatches = new CompatibilityPatchSettings { MaxOtherMastersForPatch = 1 } };
        var mods = Facts();
        var runSettings = Assert.IsType<RunRequest.Ready>(Build(settings, mods)).Settings;

        var lines = LogSections.BuildConfigSection(runSettings, ModIdentifier.Identify(mods, runSettings.ModIdentification)).Lines.ToArray();

        Assert.Contains("Compatibility patch detection: 2 plugins master the target, 1 of them compatibility patches.", lines);
        Assert.Contains(lines, line => line.StartsWith("  Plugin Merged.esp skipped: too many masters"));
        Assert.Contains("  Compatibility patch Compat.esp: links target with Other.esp.", lines);
        Assert.Contains("Ignored because of compatibility patches (the patches and the other mods they link to the target): Compat.esp, Other.esp.", lines);
    }

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
                "  Ignored 5 Target.esp objects that are initially disabled or have no valid position or rotation.",
                "  Ignored 7 other-mod objects that Target.esp itself overrides.",
            ],
            lines);
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
    public void ArchiveWarnings_ShowOneLinePerProblem()
    {
        ArchiveProblem[] problems =
        [
            new(ArchiveProblemKind.ArchiveUnreadable, "a.bsa", "unreadable"),
            new(ArchiveProblemKind.DataFolderUnlistable, "/data", "denied"),
        ];

        AssertExpectedOutput(LogSections.BuildArchiveWarningsSection(problems).Lines.ToArray(), "ArchiveWarnings_ShowOneLinePerProblem");
        Assert.Empty(LogSections.BuildArchiveWarningsSection([]).Lines);
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
            [.. LogSections.BuildTargetVisibilitySection(targets, 5, Detailed with { Shapes = shapes }).Lines, .. LogSections.BuildGroupsSection(groups, 2, Detailed).Lines],
            "TargetVisibilityAndGroups_GiveTheTextWithTheDetailedLogAndNothingWithout");
        Assert.Empty(LogSections.BuildTargetVisibilitySection(targets, 5, Normal).Lines);
        Assert.Empty(LogSections.BuildGroupsSection(groups, 2, Normal).Lines);
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
    [InlineData(ZoneShape.BoundingBox, NpcHandling.Never, false, false, true)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.AlwaysLikeAnyObject, true, false, false)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInsideTheObject, true, true, false)]
    [InlineData(ZoneShape.ObjectShape, NpcHandling.Never, true, false, true)]
    public void TooClose_PrintsTheZoneAndNpcLinesTheResultSays(ZoneShape zone, NpcHandling npcs, bool zoneLines, bool stuckLines, bool ignoredLine)
    {
        var lines = LogSections.BuildTooCloseSection(CreateTooCloseSection(zone, npcs), Detailed).Lines.ToArray();

        Assert.Equal(zoneLines, lines.Any(line => line.StartsWith("Object-shape zones:")));
        Assert.Equal(stuckLines, lines.Any(line => line.StartsWith("NPCs and creatures (only when stuck")));
        Assert.Equal(ignoredLine, lines.Any(line => line.StartsWith("NPCs and creatures: ignored;")));
    }

    [Fact]
    public void TooClose_ThrowsWhenTheStepHandledNpcsOnlyWhenStuckButGaveNoSummary()
    {
        var report = CreateTooCloseSection(ZoneShape.BoundingBox, NpcHandling.AlwaysLikeAnyObject);
        var inconsistent = report with { Result = report.Result with { Npcs = NpcHandling.OnlyWhenStuckInsideTheObject } };

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

        Assert.False(ready.Settings.TooClose.BySize);
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
        var report = CreateTooCloseSection(ZoneShape.BoundingBox, NpcHandling.Never) with { AlreadyRemoved = 1 };

        var lines = LogSections.BuildTooCloseSection(report, Normal).Lines;

        Assert.Equal("Found 1 of 3 Target.esp objects too close to other mods' objects; 1 of them were already removed on changed terrain.", lines[0]);
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
        Assert.Contains("Margin by fixed distance: on, 32 units;", TooCloseConfigLine(ConfigLines(byDistance: true)));
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
    public void Settings_TheDistanceZoneIsOffByDefaultAndPassesThroughClamped()
    {
        var defaults = new TooCloseRemovalSettings();
        Assert.False(defaults.ByDistance.Use);
        Assert.Equal(32f, defaults.ByDistance.DistanceInUnits);
        var settings = SettingsFor();
        settings.TooClose.ByDistance.Use = true;
        settings.TooClose.ByDistance.DistanceInUnits = 5000f;

        var ready = Assert.IsType<RunRequest.Ready>(Build(settings, Facts()));

        Assert.True(ready.Settings.TooClose.ByDistance);
        Assert.Equal(1024f, ready.Settings.TooClose.DistanceInUnits);
        Assert.Single(ready.Warnings);
    }

    [Fact]
    public void Config_SaysOnlyThatChangedTerrainIsOffWhileItIsOff()
    {
        var lines = ConfigLines(settings => settings.TerrainChange.HeightChangeTolerance = 0f);

        Assert.Contains("Remove objects on terrain changed by other mods: off.", lines);
    }

    [Fact]
    public void Config_GivesTheChangedTerrainChecksThatAreOn()
    {
        var lines = ConfigLines(settings => settings.TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = true, MinTerrainTypeChangePercent = 50 });

        Assert.Contains(
            "Remove objects on terrain changed by other mods: on; Which of the cleaned mod's objects are checked: VisibleObjectsOnTheGround; "
            + "Margin by object size: off; Margin by fixed distance: on, 64 units; Count height changes: on, ignoring changes up to 16 units; "
            + "Count ground type changes: on, remove only when this share of the ground points changed type: 50%.",
            lines);
    }

    [Fact]
    public void Config_LeavesOutTheChecksThatAreSwitchedOff()
    {
        var noHeight = ConfigLines(settings => settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            TerrainCheckedObjects = TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes,
            CountHeightChanges = false,
            BySize = new TerrainBySizeSettings { Use = true, SizeMultiplier = 0.5f },
            ByDistance = new TerrainByDistanceSettings { Use = false },
        });
        var noType = ConfigLines(settings => settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            TerrainCheckedObjects = TerrainCheckedObjects.VisibleObjectsAtAnyHeight,
            CountTerrainTypeChanges = false,
        });

        Assert.Contains(
            "Remove objects on terrain changed by other mods: on; Which of the cleaned mod's objects are checked: AllObjectsIncludingInvisibleOnes; "
            + "Margin by object size: on, 0.5 x object size; Margin by fixed distance: off; Count height changes: off; Count ground type changes: on, remove only when this share of the ground points changed type: 20%.",
            noHeight);
        Assert.Contains(
            "Remove objects on terrain changed by other mods: on; Which of the cleaned mod's objects are checked: VisibleObjectsAtAnyHeight; "
            + "Margin by object size: off; Margin by fixed distance: on, 64 units; Count height changes: on, ignoring changes up to 16 units; Count ground type changes: off.",
            noType);
    }

    [Fact]
    public void Config_ShowsBothTerrainMarginsWhenBothAreOn()
    {
        var lines = ConfigLines(settings => settings.TerrainChange = new TerrainChangeSettings
        {
            RemoveOnChangedTerrain = true,
            BySize = new TerrainBySizeSettings { Use = true, SizeMultiplier = 0.5f },
            ByDistance = new TerrainByDistanceSettings { Use = true, DistanceInUnits = 64f },
        });

        Assert.Contains(
            "Remove objects on terrain changed by other mods: on; Which of the cleaned mod's objects are checked: VisibleObjectsOnTheGround; "
            + "Margin by object size: on, 0.5 x object size; Margin by fixed distance: on, 64 units; Count height changes: on, ignoring changes up to 16 units; "
            + "Count ground type changes: on, remove only when this share of the ground points changed type: 20%.",
            lines);
    }

    private ImmutableArray<string> ConfigLines(bool byDistance) =>
        ConfigLines(settings => settings.TooClose.ByDistance.Use = byDistance);

    private ImmutableArray<string> ConfigLines(Action<StandardSettings> change)
    {
        var settings = SettingsFor();
        change(settings);
        var mods = Facts();
        var runSettings = Assert.IsType<RunRequest.Ready>(Build(settings, mods)).Settings;
        return LogSections.BuildConfigSection(runSettings, ModIdentifier.Identify(mods, runSettings.ModIdentification)).Lines;
    }

    private static RunRequest Build(StandardSettings settings, LoadOrderPlugins mods, bool detailedLogAndReports = false) =>
        SettingsValidator.Build(StandardConfig.Of(settings), new PluginNameParsingPlugin(), mods, detailedLogAndReports, ReportsFolder, Workers);

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

    private static TooCloseSection CreateTooCloseSection(ZoneShape zone, NpcHandling npcs) => CreateTooCloseSection(zone, npcs, [BySize], BySize);

    /// <param name="zonesTested">The zones the step reports it tested.</param>
    /// <param name="causes">One hit for each.</param>
    private static TooCloseSection CreateTooCloseSection(ZoneShape zone, NpcHandling npcs, TooCloseCause[] zonesTested, params TooCloseCause[] causes)
    {
        var world = CreateWorld();
        var zoneWork = zone == ZoneShape.ObjectShape ? new ShapeZoneWork(10, 8, 6, 3, 2, 1, 40) : default;
        var stuck = npcs == NpcHandling.OnlyWhenStuckInsideTheObject
            ? new NpcStuckSummary(new NpcSizeCounts(5, 2, 1, 1, 1, 3), 12, 30, 4, [new PointNpc(world.OtherModObjects[1], new PointReason(PointReasonKind.NoBodyMeshNoBoundsNotPlayable, TestTargets.SpaceKey(0x7000), "TestRace"))])
            : null;
        var result = new TooCloseResult(
            StepOutcome.Performed,
            [.. causes.Select(cause => new TooCloseObject(0, world.OtherModObjects[0], cause))],
            [],
            new TooCloseWork(zoneWork, default),
            stuck,
            LargeOtherObjects: 2,
            Failures: [],
            [.. zonesTested],
            zone,
            npcs);
        var census = new InvisibleOtherObjectCounts([new("Lights", 3), new("Sounds", 1)], PlacedNpcs: 7);
        var kept = new KeptObject(2, TestKeepReasons.Quest, null);
        return new TooCloseSection(
            world,
            Target.ToPluginName(),
            result,
            census,
            [kept],
            MeshesIndexed: 4,
            MeshTriangles: 900,
            BodiesBuilt: 3,
            NpcBasesResolved: 4,
            LeveledListsResolved: 6,
            BodyMeshSetsMeasured: 8,
            EffectOnlyMeshes: 9,
            Elapsed);
    }

    private static CollectedObjects CreateWorld()
    {
        var space = TestTargets.Space;
        var targets = TestTargets.CreateMany(3);
        return new CollectedObjects(
            [.. targets],
            [CreateOtherModObject(0, "Alpha"), CreateOtherModObject(1, "Beta")],
            ImmutableArray.Create(CreateOtherModObject(2, "Gamma")),
            true,
            [],
            new Dictionary<RecordKey, SpaceFact> { [space] = new(space, "Tamriel", SpaceKind.Worldspace) },
            new ReadCounts(RecordsScanned: 9, TargetsOverriddenLater: 4, TargetsHiddenOrWithoutPlacement: 5, OthersOverriddenByTarget: 7, OtherInvalidPlacements: 6, NavmeshCount: 8, RemovedByEarlierRuns: 0, MovedByEarlierRuns: 0),
            [new OverriddenOtherRecord(new FormKey(Other, 0x50).ToRecordKey(), "Overridden", Other.ToPluginName())],
            TargetWorkOrder.Of(targets),
            null);
    }

    private static OtherObject CreateOtherModObject(int index, string editorId) =>
        new(
            new OtherId(index),
            new FormKey(Other, 0x10 + (uint)index).ToRecordKey(),
            TestTargets.Space,
            Other.ToPluginName(),
            editorId,
            Base: null,
            Vector3.Zero,
            default(Vector3),
            Scale: 1f,
            IsPrimitive: false,
            HasMapMarker: false);

    private static StandardSettings SettingsFor() => new() { TargetPlugin = Target };

    private static LoadOrderPlugins Facts()
    {
        var table = new KnownPluginNames(new[] { Skyrim, Target, Other, Compat, Merged, ExtraA, ExtraB, Patch }.Select(key => key.ToPluginName()));
        PluginListing Listing(ModKey key, params ModKey[] masters) =>
            new(key.ToPluginName(), Loaded: true, [.. masters.Select(master => master.ToPluginName())]);
        return new LoadOrderPlugins(
            table,
            Patch.ToPluginName(),
            [
                Listing(Skyrim),
                Listing(Target, Skyrim),
                Listing(Other, Skyrim),
                Listing(Compat, Skyrim, Target, Other),
                Listing(Merged, Skyrim, Target, Other, ExtraA, ExtraB),
                Listing(Patch, Skyrim),
            ]);
    }

    private static void AssertExpectedOutput(IReadOnlyList<string> actual, string name, string part = "") =>
        ExpectedOutputFiles.AssertMatches("sections-" + name + (part == "" ? "" : "-" + part), actual);
}
