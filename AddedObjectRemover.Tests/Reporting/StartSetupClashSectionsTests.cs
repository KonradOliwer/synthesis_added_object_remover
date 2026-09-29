using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using AddedObjectRemover.Tests.EndToEnd;
using Noggog;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>
/// The start, setup and too-close log sections give the text checked in as golden files with the detailed log on,
/// and only what a mod user needs, without timings, with it off.
/// </summary>
public sealed class StartSetupClashSectionsTests
{
    private const string OutputPath = "/out/Patch.esp";
    private const int Workers = 4;
    private const string ArchiveWarning = "Warning: archive a.bsa is unreadable.";
    private const string MeshProblem = "[mesh] m.nif not found.";

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

    private static readonly PhaseProblems Problems = new(
        [new ArchiveProblem(ArchiveProblemKind.ArchiveUnreadable, "a.bsa", ArchiveWarning)],
        [new AssetProblem("m.nif", AssetProblemKind.NotFound, MeshProblem)]);

    private static readonly PhaseProblems NoProblems = new([], []);

    [Theory]
    [InlineData(null)]
    [InlineData("NoExtension")]
    [InlineData("Missing.esp")]
    public void Stop_GivesTheTextOfEveryUnusableTarget(string? target)
    {
        var settings = SettingsFor(target);
        var stop = Assert.IsType<OptionsResult.Stop>(OptionsBuilder.Build(settings, Facts(), OutputPath, Workers));

        AssertGolden(LogSections.Stop(stop.Reason).Lines.ToArray(), "Stop_GivesTheTextOfEveryUnusableTarget", $"{target ?? "null"}");
    }

    [Fact]
    public void Stop_SaysThereIsNothingToCheck()
    {
        Assert.Equal(
            ["Nothing to check. No changes made."],
            LogSections.Stop(new StopReason(StopKind.NoTargetObjects, null)).Lines.ToArray());
    }

    [Fact]
    public void Warnings_GiveTheTextInTheOrderTheOptionsBuilderFoundThem()
    {
        var settings = SettingsFor("Target.esp");
        settings.WhatToIgnore = new IgnoreSettings { ExcludedPlugins = ["bad"], NpcHandling = (NpcHandling)99 };
        settings.WhatToCheck.SizeMultiplier = 9f;
        settings.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings { SearchRadius = 1f };
        settings.Diagnostics = new DiagnosticsSettings { DiagnosticsFolder = "bad\0folder" };
        var ready = Assert.IsType<OptionsResult.Ready>(OptionsBuilder.Build(settings, Facts(), OutputPath, Workers));

        var lines = LogSections.Warnings(ready.Warnings).Lines.ToArray();

        Assert.Equal(ready.Warnings.Select(warning => warning.Message), lines);
        AssertGolden(lines, "Warnings_GiveTheTextInTheOrderTheOptionsBuilderFoundThem");
    }

    [Fact]
    public void ReportFolderWarning_PrintsTheMessageAsIs()
    {
        var section = LogSections.ReportFolderWarning("  Warning: deleting earlier diagnostics files in /r failed: denied");

        Assert.Equal(["  Warning: deleting earlier diagnostics files in /r failed: denied"], section.Lines.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Config_GivesTheTextWithAndWithoutPatchesAndLeftovers(bool everythingOn)
    {
        var settings = SettingsFor("Target.esp");
        settings.WhatToIgnore = new IgnoreSettings
        {
            IgnoreTargetMasters = everythingOn,
            IgnoreModsPatchedWithTarget = everythingOn,
            MaxOtherMastersForPatch = 1,
            ExcludedPlugins = everythingOn ? ["Extra.esp", "Second.esp"] : [],
        };
        settings.FollowUpRemoval = new FollowUpRemovalSettings
        {
            Mode = everythingOn ? FollowUpRemovalMode.ObjectsSupportedByIt : FollowUpRemovalMode.Nothing,
        };
        settings.LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings
        {
            RemoveLeftoverInvisibleObjects = everythingOn,
            MoveKeptMarkersOutOfOtherModsObjects = everythingOn,
        };
        settings.Diagnostics = new DiagnosticsSettings { DetailedLog = everythingOn, WriteReportFiles = everythingOn };
        var mods = Facts();
        var options = Assert.IsType<OptionsResult.Ready>(OptionsBuilder.Build(settings, mods, OutputPath, Workers)).Options;
        var standing = Standing.Decide(mods, options.Standing);

        var lines = LogSections.Config(options, mods, standing).Lines.ToArray();

        AssertGolden(lines, "Config_GivesTheTextWithAndWithoutPatchesAndLeftovers", $"{everythingOn}");
    }

    [Fact]
    public void Config_ListsSkippedPluginsAndDetectedPatchesWhenDetectionIsOn()
    {
        var settings = SettingsFor("Target.esp");
        settings.WhatToIgnore = new IgnoreSettings { MaxOtherMastersForPatch = 1 };
        var mods = Facts();
        var options = Assert.IsType<OptionsResult.Ready>(OptionsBuilder.Build(settings, mods, OutputPath, Workers)).Options;

        var lines = LogSections.Config(options, mods, Standing.Decide(mods, options.Standing)).Lines.ToArray();

        Assert.Contains("Compatibility patch detection: 2 plugins master the target.", lines);
        Assert.Contains(lines, line => line.StartsWith("  Plugin Merged.esp skipped: too many masters"));
        Assert.Contains("  Compatibility patch Compat.esp: links target with Other.esp.", lines);
        Assert.Contains("Ignored because of compatibility patches: Compat.esp, Other.esp.", lines);
    }

    [Fact]
    public void Scan_GivesTheTextWithTheDetailedLog()
    {
        var world = CreateWorld();

        var lines = LogSections.Scan(world, Target, Elapsed, Detailed).Lines.ToArray();

        AssertGolden(lines, "Scan_GivesTheTextWithTheDetailedLog");
        Assert.Contains("Scanned 9 placed records in 1.5s: 3 Target.esp objects to check, 2 objects from other mods.", lines);
    }

    [Fact]
    public void Scan_ForTheNormalLogDropsTheTimingAndTheInternalCounts()
    {
        var lines = LogSections.Scan(CreateWorld(), Target, Elapsed, Normal).Lines.ToArray();

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

        Assert.Equal(["Using 4 threads."], LogSections.Threads(execution, Detailed).Lines.ToArray());
        Assert.Empty(LogSections.Threads(execution, Normal).Lines);
    }

    [Fact]
    public void Index_GivesTheTextWithTheDetailedLogAndNothingWithout()
    {
        var world = CreateWorld();

        AssertGolden(LogSections.Index(world, Elapsed, Detailed).Lines.ToArray(), "Index_GivesTheTextWithTheDetailedLogAndNothingWithout");
        Assert.Empty(LogSections.Index(world, Elapsed, Normal).Lines);
    }

    [Fact]
    public void WarmUp_PutsTheProblemsBeforeTheSummary()
    {

        AssertGolden(LogSections.WarmUp(6, Elapsed, Problems, Detailed).Lines.ToArray(), "WarmUp_PutsTheProblemsBeforeTheSummary");
        Assert.Equal([ArchiveWarning], LogSections.WarmUp(6, Elapsed, Problems, Normal).Lines.ToArray());
        Assert.Empty(LogSections.WarmUp(6, Elapsed, NoProblems, Normal).Lines);
    }

    [Fact]
    public void TargetVisibilityAndGroups_GiveTheTextWithTheDetailedLogAndNothingWithout()
    {
        var looks = new TargetLooks([ObjectVisibility.Visible, ObjectVisibility.Invisible(InvisibleObjectKind.DoorMarkers), ObjectVisibility.MissingBase]);
        var groups = LinkedGroups.Build(3, [TestTargets.Link(0, 1)]);

        AssertGolden([.. LogSections.TargetVisibility(looks, 5, Detailed).Lines, .. LogSections.Groups(groups, 2, Detailed).Lines], "TargetVisibilityAndGroups_GiveTheTextWithTheDetailedLogAndNothingWithout");
        Assert.Empty(LogSections.TargetVisibility(looks, 5, Normal).Lines);
        Assert.Empty(LogSections.Groups(groups, 2, Normal).Lines);
    }

    [Fact]
    public void Replacements_PutTheListThenTheProblemsThenTheSummary()
    {
        var world = CreateWorld();
        var replacements = Replacements.Of(world.Rivals.Length, [new Replacement(new OtherId(0), new TargetId(1), 3.5f, 0.9f)]);

        AssertGolden(LogSections.Replacements(world, replacements, Problems, Elapsed, Detailed).Lines.ToArray(), "Replacements_PutTheListThenTheProblemsThenTheSummary");
        Assert.Equal(
            [ArchiveWarning, "Replacement matching: 1 other-mod objects excluded."],
            LogSections.Replacements(world, replacements, Problems, Elapsed, Normal).Lines.ToArray());
    }

    [Fact]
    public void TooClose_GivesTheTextForTheObjectShapeZoneWithStuckNpcs()
    {
        var report = CreateTooCloseReport(ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInObject, Problems);

        var lines = LogSections.TooClose(report, Detailed).Lines.ToArray();

        AssertGolden(lines, "TooClose_GivesTheTextForTheObjectShapeZoneWithStuckNpcs");
        Assert.Equal(1, lines.Count(line => line.StartsWith("Object-shape zones:")));
        Assert.Equal(1, lines.Count(line => line.StartsWith("  Sized as a point")));
    }

    [Fact]
    public void TooClose_GivesTheTextForTheBoundingBoxZoneWithIgnoredNpcs()
    {
        var report = CreateTooCloseReport(ZoneShape.BoundingBox, NpcHandling.Ignore, NoProblems);

        var lines = LogSections.TooClose(report, Detailed).Lines.ToArray();

        AssertGolden(lines, "TooClose_GivesTheTextForTheBoundingBoxZoneWithIgnoredNpcs");
        Assert.Contains(lines, line => line.StartsWith("NPCs and creatures: ignored;"));
    }

    [Fact]
    public void TooClose_PutsTheShapeZoneStatisticsThenTheProblemsThenTheSummary()
    {
        var lines = LogSections.TooClose(CreateTooCloseReport(ZoneShape.ObjectShape, NpcHandling.CountLikeObjects, Problems), Detailed).Lines;

        var order = new[] { "Object-shape zones:", ArchiveWarning, MeshProblem, "Found " }
            .Select(prefix => lines.ToList().FindIndex(line => line.StartsWith(prefix)))
            .ToArray();
        Assert.Equal(order.Order(), order);
        Assert.DoesNotContain(-1, order);
    }

    [Fact]
    public void TooClose_ForTheNormalLogKeepsTheResultTheArchiveProblemsAndTheKeptObjectsOnly()
    {
        var report = CreateTooCloseReport(ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInObject, Problems);

        var lines = LogSections.TooClose(report, Normal).Lines.ToArray();

        Assert.Equal(
            [
                ArchiveWarning,
                "Found 1 of 3 Target.esp objects too close to other mods' objects.",
                $"  Kept {RecordNames.Describe(report.World.Targets[2])} in Tamriel: linked from QUST.",
            ],
            lines);
    }

    private static TooCloseReport CreateTooCloseReport(ZoneShape zone, NpcHandling npcs, PhaseProblems problems)
    {
        var world = CreateWorld();
        var zoneWork = zone == ZoneShape.ObjectShape ? new ShapeZoneWork(10, 8, 6, 3, 2, 1, 40) : default;
        var stuck = npcs == NpcHandling.OnlyWhenStuckInObject
            ? new NpcStuckSummary(new NpcSizeCounts(5, 2, 1, 1, 1, 3), 12, 30, 4, [new PointNpc(world.Rivals[1], "no body mesh")])
            : null;
        var result = new ClashResult(
            [new TooCloseHit(0, world.Rivals[0])],
            [],
            new ClashWork(zoneWork, default),
            stuck,
            LargeRivals: 2);
        var census = new RivalCensus([new("Lights", 3), new("Sounds", 1)], PlacedNpcs: 7);
        var kept = new KeptTarget(2, new KeepReason(KeepKind.NonPlacedReference, "QUST record", "linked from QUST"), null);
        var perf = new ClashPerf(
            new TriangleStoreStats(4, 1, 0, 2, 900, 3, 5 * 1024 * 1024),
            new NpcBodyPerf(new NpcBodyCacheStats(3, 2, 4, 5, 6, 7), 8),
            EffectOnlyMeshes: 9);
        return new TooCloseReport(
            world,
            Target,
            new ClashOptions(1.5f, zone, npcs),
            result,
            census,
            [kept],
            problems,
            perf,
            new TooCloseTimes(Elapsed, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(2.5)));
    }

    private static World CreateWorld()
    {
        var space = TestTargets.Space;
        return new World(
            [.. TestTargets.CreateMany(3)],
            [CreateRival(0, "Alpha"), CreateRival(1, "Beta")],
            Collected<ImmutableArray<OtherObject>>.Of([CreateRival(2, "Gamma")]),
            [TestTargets.Link(0, 1)],
            [.. TestTargets.References(3)],
            new Dictionary<FormKey, string> { [space] = "Tamriel" },
            new ReadCounts(RecordsScanned: 9, TargetsOverriddenLater: 4, TargetsHiddenOrWithoutPlacement: 5, OthersOverriddenByTarget: 7, OtherInvalidPlacements: 6, TargetPluginLinks: 2, NavmeshCount: 8),
            [new OverriddenOtherRecord(new FormKey(Other, 0x50), "Overridden", Other)]);
    }

    private static OtherObject CreateRival(int index, string editorId) =>
        new(
            new OtherId(index),
            new FormKey(Other, 0x10 + (uint)index),
            TestTargets.Space,
            Other,
            editorId,
            Base: null,
            Vector3.Zero,
            default(P3Float),
            Scale: 1f,
            IsPrimitive: false,
            HasMapMarker: false);

    private static Settings SettingsFor(string? target) => new() { WhatToCheck = new CheckSettings { TargetPlugin = target! } };

    private static ModFacts Facts()
    {
        var table = new ModTable([Skyrim, Target, Other, Compat, Merged, ExtraA, ExtraB, Patch]);
        ModListing Listing(ModKey key, params ModKey[] masters) =>
            new(table.RefOf(key), Loaded: true, [.. masters.Select(table.RefOf)]);
        return new ModFacts(
            table,
            table.RefOf(Patch),
            [
                Listing(Skyrim),
                Listing(Target, Skyrim),
                Listing(Other, Skyrim),
                Listing(Compat, Skyrim, Target, Other),
                Listing(Merged, Skyrim, Target, Other, ExtraA, ExtraB),
                Listing(Patch, Skyrim),
            ]);
    }

    private static void AssertGolden(IReadOnlyList<string> actual, string name, string part = "") =>
        GoldenFiles.AssertMatches("sections-" + name + (part == "" ? "" : "-" + part), actual);
}
