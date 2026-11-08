using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Noggog;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>The settings, load orders and worlds that the tests of the start, setup, scan and too-close log sections build their sections from.</summary>
internal static class LogSectionScene
{
    private const string ReportsFolder = "/out/Reports";
    public const int Workers = 4;

    public static readonly TimeSpan Elapsed = TimeSpan.FromSeconds(1.5);
    public static readonly ModKey Target = TestTargets.TargetMod;
    private static readonly ModKey Other = ModKey.FromNameAndExtension("Other.esp");
    private static readonly ModKey Compat = ModKey.FromNameAndExtension("Compat.esp");
    private static readonly ModKey Merged = ModKey.FromNameAndExtension("Merged.esp");
    private static readonly ModKey ExtraA = ModKey.FromNameAndExtension("ExtraA.esp");
    private static readonly ModKey ExtraB = ModKey.FromNameAndExtension("ExtraB.esp");
    public static readonly ModKey Skyrim = ModKey.FromNameAndExtension("Skyrim.esm");
    public static readonly ModKey Patch = ModKey.FromNameAndExtension("Patch.esp");
    private static readonly ModKey CreationClub = ModKey.FromNameAndExtension("ccBGSSSE001-Fish.esm");
    private static readonly ModKey TargetMaster = ModKey.FromNameAndExtension("TargetMaster.esm");
    private static readonly ModKey CityMaster = ModKey.FromNameAndExtension("CityMaster.esm");
    public static readonly ModKey City = ModKey.FromNameAndExtension("City.esp");
    public static readonly ModKey Birds = ModKey.FromNameAndExtension("Birds.esp");
    public static readonly ModKey Never = ModKey.FromNameAndExtension("Never.esp");

    public static readonly ReportContext Normal = new(null!, null!, Detailed: false);
    public static readonly ReportContext Detailed = new(null!, null!, Detailed: true);
    public static readonly TooCloseCause BySize = new TooCloseCause.ObjectSize();
    public static readonly TooCloseCause WithinDistance = new TooCloseCause.Distance(32f);

    public static ImmutableArray<string> ConfigLines(bool byDistance) =>
        ConfigLines(settings => settings.TooClose.ByDistance.Use = byDistance);

    public static ImmutableArray<string> ConfigLines(Action<StandardSettings> change)
    {
        var settings = SettingsFor();
        change(settings);
        var mods = Facts();
        var runSettings = Assert.IsType<RunRequest.Ready>(Build(settings, mods)).Settings;
        return LogSections.BuildConfigSection(runSettings, ModTrustClassifier.Classify(new(mods, runSettings.ModTrust)), mods).Lines;
    }

    public static RunRequest Build(StandardSettings settings, LoadOrderPlugins mods, bool detailedLogAndReports = false) =>
        SettingsValidator.Build(StandardConfig.Of(settings), new PluginNameParsingPlugin(), mods, detailedLogAndReports, onlyWarnWhenRequiringModLoadsAfterPatch: false, ReportsFolder, Workers);

    public static TooCloseSection CreateTooCloseSection(ZoneShape zone, NpcHandling npcs) => CreateTooCloseSection(zone, npcs, [BySize], BySize);

    /// <param name="zonesTested">The zones the step reports it tested.</param>
    /// <param name="causes">One hit for each.</param>
    public static TooCloseSection CreateTooCloseSection(ZoneShape zone, NpcHandling npcs, TooCloseCause[] zonesTested, params TooCloseCause[] causes)
    {
        var world = CreateWorld();
        var zoneWork = zone == ZoneShape.ObjectShape ? new ShapeZoneWork(10, 8, 6, 3, 2, 1, 40) : default;
        var stuck = npcs is NpcHandling.OnlyWhenStuckInsideTheObject or NpcHandling.OnlyWhenStuckInsideTheObjectWithCollision
            ? new NpcStuckSummary(new NpcSizeCounts(5, 2, 1, 1, 1, 3), 12, 30, 4, 1, [new PointNpc(world.OtherModObjects[1].Id, new PointReason(PointReasonKind.NoBodyMeshNoBoundsNotPlayable, TestTargets.SpaceKey(0x7000), "TestRace"))])
            : null;
        var pass = new TooClosePass(
            [.. world.Targets.Select(target => target.Id)],
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
            [new TooClosePassReport(pass, AlreadyRemoved: 0, Restored: 0)],
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

    public static CollectedObjects CreateWorld()
    {
        var space = TestTargets.Space;
        var targets = TestTargets.CreateMany(3);
        return new CollectedObjects(
            [.. targets],
            [CreateOtherModObject(0, "Alpha"), CreateOtherModObject(1, "Beta")],
            ImmutableArray.Create(CreateOtherModObject(2, "Gamma")),
            [],
            [],
            [],
            [],
            [],
            [],
            new Dictionary<RecordKey, SpaceFact> { [space] = new(space, "Tamriel", SpaceKind.Worldspace, "Skyrim") },
            new ReadCounts(
                RecordsScanned: 9,
                CleanedModObjects: 16,
                TargetsOverriddenLater: 4,
                TargetsHiddenOrWithoutPlacement: 5,
                RemovedByEarlierPatchers: 1,
                RemovedByOtherPlugins: 3,
                OthersOverriddenByTarget: 7,
                OthersUnchanged: 10,
                OtherInvalidPlacements: 6,
                NavmeshCount: 8),
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
            HasMapMarker: false,
            new ObjectChange(Other.ToPluginName(), AcceptedFrom: null),
            ReferenceRoles.None,
            ChangeWithoutCleanedMod: null,
            WinningModPastPatches: Other.ToPluginName());

    public static StandardSettings SettingsFor() => new() { CleanedMod = Target };

    public static LoadOrderPlugins Facts()
    {
        var table = new KnownPluginNames(new[] { Skyrim, Target, Other, Compat, Merged, ExtraA, ExtraB, Patch }.Select(key => key.ToPluginName()));
        PluginListing Listing(ModKey key, params ModKey[] masters) =>
            new(key.ToPluginName(), Loaded: true, [.. masters.Select(master => master.ToPluginName())]);
        return TestLoadOrderPlugins.Of(
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

    /// <summary>
    /// Before the cleaned mod: the base game, a Creation Club plugin and the cleaned mod's master. After it: a city mod with its
    /// master, a bird mod, a mod to be never trusted, a compatibility patch with one other master, and a merged plugin that
    /// requires the cleaned mod and six plugins that are not in the plugin list.
    /// </summary>
    public static LoadOrderPlugins TrustFacts()
    {
        PluginListing Listing(ModKey key, params ModKey[] masters) =>
            new(key.ToPluginName(), Loaded: true, [.. masters.Select(master => master.ToPluginName())]);
        ModKey[] mergedMasters = [Skyrim, Target, .. Enumerable.Range(1, 6).Select(number => ModKey.FromNameAndExtension($"Part{number}.esp"))];
        PluginListing[] listed =
        [
            Listing(Skyrim),
            Listing(CreationClub, Skyrim),
            Listing(TargetMaster, Skyrim),
            Listing(Target, Skyrim, TargetMaster),
            Listing(CityMaster, Skyrim),
            Listing(City, Skyrim, CityMaster),
            Listing(Birds, Skyrim),
            Listing(Never, Skyrim),
            Listing(Other, Skyrim),
            Listing(Compat, Skyrim, Target, Other),
            Listing(Merged, mergedMasters),
            Listing(Patch, Skyrim),
        ];
        return TestLoadOrderPlugins.Of(
            new KnownPluginNames(listed.SelectMany(listing => listing.Masters.Prepend(listing.Mod))), Patch.ToPluginName(), [.. listed]);
    }

    public static void AssertExpectedOutput(IReadOnlyList<string> actual, string name, string part = "") =>
        ExpectedOutputFiles.AssertMatches("sections-" + name + (part == "" ? "" : "-" + part), actual);
}
