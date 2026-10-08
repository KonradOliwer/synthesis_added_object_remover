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

    private static readonly PhaseProblems Problems = new([new AssetProblem("m.nif", AssetProblemKind.NotFound, MeshProblem)]);

    private static readonly PhaseProblems NoProblems = new([]);

    [Theory]
    [InlineData(null)]
    [InlineData("NoExtension")]
    [InlineData("Missing.esp")]
    public void Stop_GivesTheTextOfEveryUnusableTarget(string? target)
    {
        var settings = SettingsFor(target);
        var stop = Assert.IsType<RunRequest.Stop>(RunSettingsValidation.Build(settings, new PluginNameParsingPlugin(), Facts(), OutputPath, Workers));

        AssertExpectedOutput(LogSections.Stop(stop.Reason).Lines.ToArray(), "Stop_GivesTheTextOfEveryUnusableTarget", $"{target ?? "null"}");
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
        var ready = Assert.IsType<RunRequest.Ready>(RunSettingsValidation.Build(settings, new PluginNameParsingPlugin(), Facts(), OutputPath, Workers));

        var lines = LogSections.Warnings(ready.Warnings).Lines.ToArray();

        Assert.Equal(ready.Warnings.Select(warning => warning.Message), lines);
        AssertExpectedOutput(lines, "Warnings_GiveTheTextInTheOrderTheOptionsBuilderFoundThem");
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
        var options = Assert.IsType<RunRequest.Ready>(RunSettingsValidation.Build(settings, new PluginNameParsingPlugin(), mods, OutputPath, Workers)).Options;
        var standing = IdentifyTheMods.Decide(mods, options.ModIdentification);

        var lines = LogSections.Config(options, standing).Lines.Select(line => line.Replace('\\', '/')).ToArray();

        AssertExpectedOutput(lines, "Config_GivesTheTextWithAndWithoutPatchesAndLeftovers", $"{everythingOn}");
    }

    [Fact]
    public void Config_ListsSkippedPluginsAndDetectedPatchesWhenDetectionIsOn()
    {
        var settings = SettingsFor("Target.esp");
        settings.WhatToIgnore = new IgnoreSettings { MaxOtherMastersForPatch = 1 };
        var mods = Facts();
        var options = Assert.IsType<RunRequest.Ready>(RunSettingsValidation.Build(settings, new PluginNameParsingPlugin(), mods, OutputPath, Workers)).Options;

        var lines = LogSections.Config(options, IdentifyTheMods.Decide(mods, options.ModIdentification)).Lines.ToArray();

        Assert.Contains("Compatibility patch detection: 2 plugins master the target.", lines);
        Assert.Contains(lines, line => line.StartsWith("  Plugin Merged.esp skipped: too many masters"));
        Assert.Contains("  Compatibility patch Compat.esp: links target with Other.esp.", lines);
        Assert.Contains("Ignored because of compatibility patches: Compat.esp, Other.esp.", lines);
    }

    [Fact]
    public void Scan_GivesTheTextWithTheDetailedLog()
    {
        var world = CreateWorld();

        var lines = LogSections.Scan(world, Target.ToPluginName(), Elapsed, Detailed).Lines.ToArray();

        AssertExpectedOutput(lines, "Scan_GivesTheTextWithTheDetailedLog");
        Assert.Contains("Scanned 9 placed records in 1.5s: 3 Target.esp objects to check, 2 objects from other mods.", lines);
    }

    [Fact]
    public void Scan_ForTheNormalLogDropsTheTimingAndTheInternalCounts()
    {
        var lines = LogSections.Scan(CreateWorld(), Target.ToPluginName(), Elapsed, Normal).Lines.ToArray();

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

        AssertExpectedOutput(LogSections.Index(world, Elapsed, Detailed).Lines.ToArray(), "Index_GivesTheTextWithTheDetailedLogAndNothingWithout");
        Assert.Empty(LogSections.Index(world, Elapsed, Normal).Lines);
    }

    [Fact]
    public void ArchiveWarnings_ShowOneLinePerProblem()
    {
        ArchiveProblem[] problems =
        [
            new(ArchiveProblemKind.ArchiveUnreadable, "a.bsa", ArchiveWarning),
            new(ArchiveProblemKind.DataFolderUnlistable, "/data", "Warning: could not list archives in /data: denied"),
        ];

        AssertExpectedOutput(LogSections.ArchiveWarnings(problems).Lines.ToArray(), "ArchiveWarnings_ShowOneLinePerProblem");
        Assert.Empty(LogSections.ArchiveWarnings([]).Lines);
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
            [.. LogSections.TargetVisibility(targets, 5, Detailed with { Shapes = shapes }).Lines, .. LogSections.Groups(groups, 2, Detailed).Lines],
            "TargetVisibilityAndGroups_GiveTheTextWithTheDetailedLogAndNothingWithout");
        Assert.Empty(LogSections.TargetVisibility(targets, 5, Normal).Lines);
        Assert.Empty(LogSections.Groups(groups, 2, Normal).Lines);
    }

    [Fact]
    public void Replacements_PutTheListThenTheProblemsThenTheSummary()
    {
        var world = CreateWorld();
        var replacements = Replacements.Of(world.OtherModObjects.Length, [new Replacement(new OtherId(0), new TargetId(1), 3.5f, 0.9f)]);

        AssertExpectedOutput(LogSections.Replacements(world, replacements, Problems, Elapsed, Detailed).Lines.ToArray(), "Replacements_PutTheListThenTheProblemsThenTheSummary");
        Assert.Equal(
            ["Replacement matching: 1 other-mod objects excluded."],
            LogSections.Replacements(world, replacements, Problems, Elapsed, Normal).Lines.ToArray());
    }

    [Fact]
    public void TooClose_GivesTheTextForTheObjectShapeZoneWithStuckNpcs()
    {
        var report = CreateTooCloseSection(ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInObject, Problems);

        var lines = LogSections.TooClose(report, Detailed).Lines.ToArray();

        AssertExpectedOutput(lines, "TooClose_GivesTheTextForTheObjectShapeZoneWithStuckNpcs");
        Assert.Equal(1, lines.Count(line => line.StartsWith("Object-shape zones:")));
        Assert.Equal(1, lines.Count(line => line.StartsWith("  Sized as a point")));
    }

    [Fact]
    public void TooClose_GivesTheTextForTheBoundingBoxZoneWithIgnoredNpcs()
    {
        var report = CreateTooCloseSection(ZoneShape.BoundingBox, NpcHandling.Ignore, NoProblems);

        var lines = LogSections.TooClose(report, Detailed).Lines.ToArray();

        AssertExpectedOutput(lines, "TooClose_GivesTheTextForTheBoundingBoxZoneWithIgnoredNpcs");
        Assert.Contains(lines, line => line.StartsWith("NPCs and creatures: ignored;"));
    }

    [Fact]
    public void TooClose_PutsTheShapeZoneStatisticsThenTheProblemsThenTheSummary()
    {
        var lines = LogSections.TooClose(CreateTooCloseSection(ZoneShape.ObjectShape, NpcHandling.CountLikeObjects, Problems), Detailed).Lines;

        var order = new[] { "Object-shape zones:", MeshProblem, "Found " }
            .Select(prefix => lines.ToList().FindIndex(line => line.StartsWith(prefix)))
            .ToArray();
        Assert.Equal(order.Order(), order);
        Assert.DoesNotContain(-1, order);
    }

    [Fact]
    public void TooClose_ForTheNormalLogKeepsTheResultAndTheKeptObjectsOnly()
    {
        var report = CreateTooCloseSection(ZoneShape.ObjectShape, NpcHandling.OnlyWhenStuckInObject, Problems);

        var lines = LogSections.TooClose(report, Normal).Lines.ToArray();

        Assert.Equal(
            [
                "Found 1 of 3 Target.esp objects too close to other mods' objects.",
                $"  Kept {RecordNames.Describe(report.World.Targets[2])} in {Describe.Space(report.World, TestTargets.Space)}: {TestKeepReasons.QuestDetail}.",
            ],
            lines);
    }

    private static TooCloseSection CreateTooCloseSection(ZoneShape zone, NpcHandling npcs, PhaseProblems problems)
    {
        var world = CreateWorld();
        var zoneWork = zone == ZoneShape.ObjectShape ? new ShapeZoneWork(10, 8, 6, 3, 2, 1, 40) : default;
        var stuck = npcs == NpcHandling.OnlyWhenStuckInObject
            ? new NpcStuckSummary(new NpcSizeCounts(5, 2, 1, 1, 1, 3), 12, 30, 4, [new PointNpc(world.OtherModObjects[1], new PointReason(PointReasonKind.NoBodyMeshNoBoundsNotPlayable, TestTargets.SpaceKey(0x7000), "TestRace"))])
            : null;
        var result = new TooCloseResult(
            [new TooCloseObject(0, world.OtherModObjects[0])],
            [],
            new TooCloseWork(zoneWork, default),
            stuck,
            LargeOtherObjects: 2,
            Failures: []);
        var census = new InvisibleOtherObjectCounts([new("Lights", 3), new("Sounds", 1)], PlacedNpcs: 7);
        var kept = new KeptObject(2, TestKeepReasons.Quest, null);
        return new TooCloseSection(
            world,
            Target.ToPluginName(),
            new TooCloseOptions(1.5f, zone, npcs),
            result,
            census,
            [kept],
            problems,
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
        return new CollectedObjects(
            [.. TestTargets.CreateMany(3)],
            [CreateOtherModObject(0, "Alpha"), CreateOtherModObject(1, "Beta")],
            ImmutableArray.Create(CreateOtherModObject(2, "Gamma")),
            [],
            new Dictionary<RecordKey, SpaceFact> { [space] = new(space, "Tamriel", SpaceKind.Worldspace) },
            new ReadCounts(RecordsScanned: 9, TargetsOverriddenLater: 4, TargetsHiddenOrWithoutPlacement: 5, OthersOverriddenByTarget: 7, OtherInvalidPlacements: 6, NavmeshCount: 8),
            [new OverriddenOtherRecord(new FormKey(Other, 0x50).ToRecordKey(), "Overridden", Other.ToPluginName())]);
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

    private static Settings SettingsFor(string? target) => new() { WhatToCheck = new CheckSettings { TargetPlugin = target! } };

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
