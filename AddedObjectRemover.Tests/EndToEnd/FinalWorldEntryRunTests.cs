using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Runs through the entry with plugin files on disk below the patch, on a small world of its own: the cleaned mod's crate in the
/// interior, and another mod's pad far from it. A plugin that moves the pad next to the crate makes the crate too close.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public sealed class FinalWorldEntryRunTests : IDisposable
{
    private const int Workers = 4;
    private const string BaseGamePlugin = "Skyrim.esm";
    private const string OtherPlugin = "Other.esp";
    private const string BelowPlugin = "Below.esp";
    private const string MissingPlugin = "Missing.esp";
    private const string RequiringPlugin = "Requiring.esp";
    private const string RemovedLineMark = "disabled True";
    private const string PatchMarker = "Added Object Remover patch";
    private const float FarPadCentre = 2000f;
    private const float FarCrateDistance = 1000f;

    /// <summary>The pad's near side 5 units from the 100-unit crate: inside the size margin of 10 units.</summary>
    private const float NearPadCentre = 75f;

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("aor-final-world-entry-");
    private readonly string _dataFolder;
    private readonly string _outputFolder;
    private readonly FixtureMod _baseGame = new(BaseGamePlugin);
    private readonly FixtureMod _target;
    private readonly FixtureMod _other;
    private readonly FixtureSpaces _spaces;
    private readonly PlacedObject _pad;
    private readonly PlacedObject _crate;

    public FinalWorldEntryRunTests()
    {
        _dataFolder = Directory.CreateDirectory(Path.Combine(_root.FullName, "Data")).FullName;
        _outputFolder = Directory.CreateDirectory(Path.Combine(_root.FullName, "Output")).FullName;
        FixtureMeshes.WriteAll(_dataFolder);
        _target = new FixtureMod(FixtureWorld.TargetName, _baseGame);
        _other = new FixtureMod(OtherPlugin, _baseGame);
        _spaces = FixtureSpaces.CreateIn(_baseGame);
        _spaces.AddOverridesTo(_target);
        _spaces.AddOverridesTo(_other);
        _crate = _target.Interior.Place("T_Crate", _target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel), Vector3.Zero);
        _pad = _other.Interior.Place("O_Pad", _other.AddModeledStatic("O_PadBase", FixtureMeshes.PadModel), new Vector3(FarPadCentre, 0, 0));
    }

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public void APluginBelowThePatchIsReadFromItsFile()
    {
        var below = WritePadMover(BelowPlugin);

        var run = Run([below], patchEnabled: true);

        Assert.Single(run.Patch, line => line.Contains(RemovedLineMark, StringComparison.Ordinal));
    }

    [Fact]
    public void TheRunWritesThePatchMarkerIntoThePatchDescription()
    {
        var run = Run([WriteEmptyPlugin(BelowPlugin)], patchEnabled: true);

        Assert.Equal(PatchMarker, run.PatchDescription);
    }

    /// <summary>An old copy of the patch moves the pad next to the crate; the crate stays because that copy is never read.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnOldCopyOfThePatchOnDiskIsNeverRead(bool patchEnabled)
    {
        WritePadMover(FixturePatcherState.PatchModKey.FileName);
        var below = WriteEmptyPlugin(BelowPlugin);

        var run = Run([below], patchEnabled);

        Assert.Empty(run.Patch);
        Assert.Contains(run.Log, line => line.StartsWith("Done", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEnabledPluginBelowThePatchWithoutAFileIsSkippedWithAWarning()
    {
        var run = Run([new SkyrimMod(ModKey.FromNameAndExtension(MissingPlugin), SkyrimRelease.SkyrimSE)], patchEnabled: true);

        Assert.Contains($"Warning: {MissingPlugin} is enabled in your plugin list but its file was not found; it was skipped.", run.Log);
    }

    [Fact]
    public void AModRequiringTheCleanedModBelowThePatchStopsTheRun()
    {
        var run = Run([WriteRequiringPlugin(RequiringPlugin)], patchEnabled: true);

        var patchFile = FixturePatcherState.PatchModKey.FileName;
        Assert.Contains(
            run.Log,
            line => line.StartsWith($"{RequiringPlugin} requires {FixtureWorld.TargetName} and loads after the patch {patchFile} (position ", StringComparison.Ordinal)
                && line.EndsWith(
                    $"): move {patchFile} below it in your plugin list, or turn on \"Only warn (don't stop) when a mod that requires the cleaned mod loads after the patch\". No changes made.",
                    StringComparison.Ordinal));
        Assert.Empty(run.Patch);
    }

    /// <summary>
    /// A pad next to the crate would get the crate removed for being too close, but the requiring mod overrides the crate, so the
    /// patch leaves it unchanged. A second crate far from every pad gives the run something to check.
    /// </summary>
    [Fact]
    public void WithTheSwitchOnAModRequiringTheCleanedModBelowThePatchIsAWarningAndTheRunGoesOn()
    {
        _target.Interior.Place("T_FarCrate", _target.AddModeledStatic("T_FarCrateBase", FixtureMeshes.CrateModel), new Vector3(0, FarCrateDistance, 0));
        _other.Interior.Place("O_NearPad", _pad.Base.FormKey, new Vector3(NearPadCentre, 0, 0));

        var run = Run([WriteRequiringPlugin(RequiringPlugin)], patchEnabled: true, onlyWarn: true);

        Assert.Contains(
            run.Log,
            line => line.StartsWith(
                    $"Warning: {RequiringPlugin} requires {FixtureWorld.TargetName} and loads after the patch {FixturePatcherState.PatchModKey.FileName} (position ",
                    StringComparison.Ordinal)
                && line.EndsWith($"); the objects of {FixtureWorld.TargetName} that it overrides are left unchanged.", StringComparison.Ordinal));
        Assert.Contains(run.Log, line => line.StartsWith("Done", StringComparison.Ordinal));
        Assert.Empty(run.Patch);
    }

    /// <summary>The marker survives the plugin file: the reader finds it in a plugin imported from disk, not only in an in-memory one.</summary>
    [Fact]
    public void APluginOnDiskCarryingThePatchMarkerIsNamedAsAPatchOfAnotherGroup()
    {
        var requiring = WriteRequiringPlugin(RequiringPlugin, PatchMarker);

        var run = Run([requiring], patchEnabled: true, onlyWarn: true);

        Assert.Contains($"Requires {FixtureWorld.TargetName} but not trusted, Added Object Remover patch of another group: {RequiringPlugin}", run.Log);
    }

    private sealed record FinishedRun(IReadOnlyList<string> Log, IReadOnlyList<string> Patch, string? PatchDescription);

    /// <param name="belowPatch">The plugins the plugin list names below the patch.</param>
    private FinishedRun Run(IReadOnlyList<IModKeyed> belowPatch, bool patchEnabled, bool onlyWarn = false)
    {
        var state = FixturePatcherState.Create(
            [_baseGame.Mod, _target.Mod, _other.Mod], _dataFolder, Path.Combine(_outputFolder, FixturePatcherState.PatchModKey.FileName));
        var pluginsFile = FixturePatcherState.WritePluginsFile(state, belowPatch, patchEnabled);
        var log = PipelineRun.CaptureConsole(() => Program.RunPatch(state, () => TooCloseOnly(onlyWarn), Workers, pluginsFile));
        return new FinishedRun(log, PatchDump.Describe(state.PatchMod), state.PatchMod.ModHeader.Description);
    }

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

    /// <summary>Writes a plugin whose copy of the other mod's pad stands next to the crate.</summary>
    private ISkyrimModGetter WritePadMover(string fileName)
    {
        var mover = new FixtureMod(fileName, _baseGame, _other);
        _spaces.AddOverridesTo(mover);
        mover.Interior.PlaceCopyOf(_pad).Placement!.Position = new P3Float(NearPadCentre, 0, 0);
        return Write(mover.Mod);
    }

    private ISkyrimModGetter WriteEmptyPlugin(string fileName) => Write(new SkyrimMod(ModKey.FromNameAndExtension(fileName), SkyrimRelease.SkyrimSE));

    /// <summary>Writes a plugin that overrides the cleaned mod's crate, so the file lists the cleaned mod as a master.</summary>
    private ISkyrimModGetter WriteRequiringPlugin(string fileName, string? description = null)
    {
        var requiring = new FixtureMod(fileName, _baseGame, _target);
        requiring.Mod.ModHeader.Description = description;
        _spaces.AddOverridesTo(requiring);
        requiring.Interior.PlaceCopyOf(_crate);
        return Write(requiring.Mod);
    }

    private ISkyrimModGetter Write(SkyrimMod plugin)
    {
        plugin.WriteToBinary(Path.Combine(_dataFolder, plugin.ModKey.FileName));
        return plugin;
    }
}
