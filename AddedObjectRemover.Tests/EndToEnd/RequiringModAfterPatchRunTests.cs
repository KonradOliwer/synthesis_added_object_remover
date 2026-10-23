using System.Numerics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Runs with a mod that requires the cleaned mod below the patch, on a small world of its own: two crates of the cleaned mod in the
/// interior, each with another mod's pad 5 units from it, so both would be removed for being too close. The requiring mod overrides one.
/// </summary>
public sealed class RequiringModAfterPatchRunTests
{
    private const int Workers = 4;

    /// <summary>The base game, the cleaned mod and the other mod load above the patch; the requiring mod loads below it.</summary>
    private const int AbovePatch = 3;

    private const string BaseGamePlugin = "Skyrim.esm";
    private const string OtherPlugin = "Other.esp";
    private const string RequiringPlugin = "Requiring.esp";
    private const string OverriddenCrate = "T_OverriddenCrate";
    private const string OtherCrate = "T_OtherCrate";
    private const string StopsSection = "stop";
    private const string WarningsSection = "warnings";
    private const string RemovedLineMark = "disabled True";
    private const float RowSpacing = 1000f;

    /// <summary>The pad's near side 5 units from the 100-unit crate: inside the size margin of 10 units.</summary>
    private const float PadCentre = 75f;

    [Fact]
    public void AModRequiringTheCleanedModBelowThePatchStopsTheRunAndChangesNothing()
    {
        var log = new RecordingLogSink();
        IReadOnlyList<string> patchLines = [RemovedLineMark];

        var result = FixtureRun.Run(
            TooCloseOnly(onlyWarn: false),
            Workers,
            log,
            OpenMeshFiles,
            createLoadOrder: CreateWorld,
            patchPosition: AbovePatch,
            inspectPatch: patch => patchLines = PatchDump.Describe(patch));

        Assert.Equal(
            new StopReason(StopKind.RequiringModLoadsAfterPatch, FixtureWorld.TargetName, new PatchPlacement(FixturePatcherState.PatchModKey.FileName, RequiringPlugin, AbovePatch + 1, AbovePatch)),
            Assert.IsType<RunResult.Stopped>(result).Reason);
        Assert.Equal(
            [$"{RequiringPlugin} requires {FixtureWorld.TargetName} and loads after the patch {FixturePatcherState.PatchModKey.FileName} (position 4 > 3): move {FixturePatcherState.PatchModKey.FileName} below it in your plugin list, or turn on \"Only warn (don't stop) when a mod that requires the cleaned mod loads after the patch\". No changes made."],
            LinesOf(log, StopsSection));
        Assert.Empty(patchLines);
    }

    /// <summary>The objects the requiring mod overrides are left to it; the cleaned mod's other crate is judged and removed as usual.</summary>
    [Fact]
    public void WithTheSwitchOnTheRunGoesOnAndLeavesTheObjectsTheRequiringModOverridesUnchanged()
    {
        var log = new RecordingLogSink();
        IReadOnlyList<string> patchLines = [];

        var outcome = ((RunResult.Done)FixtureRun.Run(
            TooCloseOnly(onlyWarn: true),
            Workers,
            log,
            OpenMeshFiles,
            createLoadOrder: CreateWorld,
            patchPosition: AbovePatch,
            inspectPatch: patch => patchLines = PatchDump.Describe(patch))).Outcome;

        Assert.Equal(
            [$"Warning: {RequiringPlugin} requires {FixtureWorld.TargetName} and loads after the patch {FixturePatcherState.PatchModKey.FileName} (position 4 > 3); the objects of {FixtureWorld.TargetName} that it overrides are left unchanged."],
            LinesOf(log, WarningsSection));
        Assert.Equal(1, outcome.Collected.Counts.TargetsOverriddenLater);
        Assert.Equal([OtherCrate], outcome.Collected.Targets.Select(target => target.EditorId));
        Assert.Single(patchLines, line => line.Contains(RemovedLineMark, StringComparison.Ordinal));
    }

    private static string[] LinesOf(RecordingLogSink log, string sectionId) =>
        [.. log.Sections.Where(section => section.Id == sectionId).SelectMany(section => section.Lines)];

    private static Settings TooCloseOnly(bool onlyWarn) => new()
    {
        OnlyWarnWhenModRequiringCleanedModLoadsAfterPatch = onlyWarn,
        Standard = new StandardSettings
        {
            CleanedMod = ModKey.FromNameAndExtension(FixtureWorld.TargetName),
            TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = false },
            TooClose = new TooCloseRemovalSettings
            {
                BySize = new TooCloseBySizeSettings { Use = true, SizeMultiplier = 0.1f },
                ByDistance = new TooCloseByDistanceSettings { Use = true, DistanceInUnits = 32f },
            },
        },
    };

    private static IMeshFilesFactory OpenMeshFiles(string dataFolder, GameRelease release, IReadOnlyList<ModKey> listedPlugins) =>
        new MeshFilesFactory(dataFolder, release, listedPlugins);

    private static IReadOnlyList<ISkyrimMod> CreateWorld(string dataFolder)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var baseGame = new FixtureMod(BaseGamePlugin);
        var target = new FixtureMod(FixtureWorld.TargetName, baseGame);
        var other = new FixtureMod(OtherPlugin, baseGame);
        var requiring = new FixtureMod(RequiringPlugin, baseGame, target);
        var spaces = FixtureSpaces.CreateIn(baseGame);
        foreach (var mod in new[] { target, other, requiring }) spaces.AddOverridesTo(mod);

        var crate = target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel);
        var pad = other.AddModeledStatic("O_PadBase", FixtureMeshes.PadModel);
        var overridden = target.Interior.Place(OverriddenCrate, crate, Vector3.Zero);
        target.Interior.Place(OtherCrate, crate, new Vector3(0, RowSpacing, 0));
        other.Interior.Place("O_PadOfTheFirstCrate", pad, new Vector3(PadCentre, 0, 0));
        other.Interior.Place("O_PadOfTheSecondCrate", pad, new Vector3(PadCentre, RowSpacing, 0));
        requiring.Interior.PlaceCopyOf(overridden);
        return [baseGame.Mod, target.Mod, other.Mod, requiring.Mod];
    }
}
