using System.Numerics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Runs with plugins below the patch, on small worlds of their own: the cleaned mod's crates in the interior, and one crate in
/// the exterior cell with another mod's pad 5 units from it, so that crate is removed for being too close.
/// </summary>
public sealed class PatchInTheMiddleRunTests : IDisposable
{
    private const int Workers = 4;
    private const string BaseGamePlugin = "Skyrim.esm";
    private const string OtherPlugin = "Other.esp";
    private const string AbovePlugin = "Above.esp";
    private const string BelowPlugin = "Below.esp";
    private const string BelowPluginArchive = "Below.bsa";
    private const string ArchiveOutsideTheWorld = "Elsewhere.bsa";
    private const string OwnCrate = "T_OwnCrate";
    private const string CrateOverriddenAbove = "T_CrateOverriddenAbove";
    private const string CrateOverriddenBelow = "T_CrateOverriddenBelow";
    private const string ExteriorCrate = "T_ExteriorCrate";
    private const string ArchiveWarningsSection = "archiveWarnings";
    private const string RemovedLineMark = "disabled True";
    private const string PersistentLineMark = "(persistent)";
    private const float RowSpacing = 1000f;
    private const float CrateCorner = 1000f;

    /// <summary>The pad's near side 5 units from the 100-unit crate: inside the size margin of 10 units.</summary>
    private const float PadCentre = 75f;

    /// <summary>An archive cut off after its "BSA" mark and version 105: reading it fails, which the log reports by the archive's name.</summary>
    private static readonly byte[] UnreadableArchive = [(byte)'B', (byte)'S', (byte)'A', 0, 105, 0, 0, 0];

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("aor-patch-middle-");

    /// <summary>What a plugin below the patch overrides with a link to a record of its own.</summary>
    public enum OverriddenBelow { Cell, Worldspace }

    /// <summary>Where the cleaned mod's crate stands in the exterior: in the grid cell, or as a persistent reference in the worldspace's persistent cell.</summary>
    private enum CrateStandsIn { ExteriorCell, PersistentCell }

    public static TheoryData<OverriddenBelow, int> MastersCases => new()
    {
        { OverriddenBelow.Cell, 1 },
        { OverriddenBelow.Cell, 2 },
        { OverriddenBelow.Worldspace, 1 },
        { OverriddenBelow.Worldspace, 2 },
    };

    public void Dispose() => _folder.Delete(recursive: true);

    [Fact]
    public void ACleanedModObjectThatAPluginAboveOrBelowThePatchOverridesIsOverriddenLater()
    {
        var outcome = ((RunResult.Done)FixtureRun.Run(
            TooCloseOnlyWarningOfRequiringMods(), Workers, new RecordingLogSink(), OpenMeshFiles, createLoadOrder: CreateOverridingWorld, patchPosition: 3)).Outcome;

        Assert.Equal(2, outcome.Collected.Counts.TargetsOverriddenLater);
        Assert.Equal([OwnCrate], outcome.Collected.Targets.Select(target => target.EditorId));
    }

    [Fact]
    public void TheArchiveOfAPluginBelowThePatchIsRead()
    {
        var log = new RecordingLogSink();

        FixtureRun.Run(TooCloseOnlyWarningOfRequiringMods(), Workers, log, OpenMeshFiles, createLoadOrder: CreateWorldWithUnreadableArchives, patchPosition: 3);

        var archiveWarnings = log.Sections.Where(section => section.Id == ArchiveWarningsSection).SelectMany(section => section.Lines).ToList();
        Assert.Single(archiveWarnings, line => line.Contains(BelowPluginArchive, StringComparison.Ordinal));
        Assert.DoesNotContain(archiveWarnings, line => line.Contains(ArchiveOutsideTheWorld, StringComparison.Ordinal));
    }

    /// <summary>The patch takes its cell and worldspace copies from the plugins above it, so a plugin below it never becomes its master.</summary>
    [Theory]
    [MemberData(nameof(MastersCases))]
    public void ARemovalWhereAPluginBelowThePatchWinsTheCellOrWorldspaceGivesThePatchNoMasterBelowIt(OverriddenBelow overridden, int pluginsBelow)
    {
        var patch = RunWithPluginsBelow(dataFolder => CreateWorldOverriddenBelow(dataFolder, overridden, pluginsBelow));

        Assert.Single(patch.Lines, line => line.Contains(RemovedLineMark, StringComparison.Ordinal));
        AssertNoMasterBelowThePatch(patch.Masters, pluginsBelow);
    }

    /// <summary>
    /// The object is persistent in the worldspace's persistent cell, which the patch copies from the plugin above it that wins that
    /// cell; the plugin below the patch overrides the worldspace or the exterior cell next to it.
    /// </summary>
    [Theory]
    [MemberData(nameof(MastersCases))]
    public void ARemovedPersistentObjectOfAWorldspacesPersistentCellIsWrittenToThePatchWithNoMasterBelowIt(OverriddenBelow overridden, int pluginsBelow)
    {
        var patch = RunWithPluginsBelow(
            dataFolder => CreateWorldOverriddenBelow(dataFolder, overridden, pluginsBelow, CrateStandsIn.PersistentCell));

        Assert.Single(patch.Lines, line => line.Contains($"{PersistentLineMark}: {RemovedLineMark}", StringComparison.Ordinal));
        AssertNoMasterBelowThePatch(patch.Masters, pluginsBelow);
    }

    /// <param name="Lines">The patch's placed-record overrides, as <see cref="PatchDump"/> describes them.</param>
    /// <param name="Masters">The masters of the patch once it is written to a file.</param>
    private sealed record PatchAfterRun(IReadOnlyList<string> Lines, IReadOnlyList<ModKey> Masters);

    private PatchAfterRun RunWithPluginsBelow(Func<string, IReadOnlyList<ISkyrimMod>> createLoadOrder)
    {
        var patch = new PatchAfterRun([], []);

        FixtureRun.Run(
            TooCloseOnly(),
            Workers,
            new RecordingLogSink(),
            OpenMeshFiles,
            createLoadOrder: createLoadOrder,
            patchPosition: 3,
            inspectPatch: written => patch = new PatchAfterRun(PatchDump.Describe(written), MastersOfWritten(written)));

        return patch;
    }

    private static void AssertNoMasterBelowThePatch(IReadOnlyList<ModKey> masters, int pluginsBelow)
    {
        Assert.DoesNotContain(masters, master => BelowPluginNames(pluginsBelow).Contains(master.FileName.String));
        Assert.Contains(masters, master => master.FileName == FixtureWorld.TargetName);
    }

    private IReadOnlyList<ModKey> MastersOfWritten(ISkyrimModGetter patch)
    {
        var path = Path.Combine(_folder.FullName, patch.ModKey.FileName);
        patch.WriteToBinary(path);
        using var written = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        return [.. written.MasterReferences.Select(master => master.Master)];
    }

    private static Settings TooCloseOnly() => new()
    {
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

    /// <summary>The worlds with an overriding plugin below the patch: that plugin requires the cleaned mod, which would stop the run.</summary>
    private static Settings TooCloseOnlyWarningOfRequiringMods()
    {
        var settings = TooCloseOnly();
        settings.OnlyWarnWhenModRequiringCleanedModLoadsAfterPatch = true;
        return settings;
    }

    private static IMeshFilesFactory OpenMeshFiles(string dataFolder, GameRelease release, IReadOnlyList<ModKey> listedPlugins) =>
        new MeshFilesFactory(dataFolder, release, listedPlugins);

    private static string[] BelowPluginNames(int count) => [.. Enumerable.Range(1, count).Select(number => $"Below{number}.esp")];

    /// <summary>Base game, cleaned mod, a plugin above the patch and one below it, each overriding one of the cleaned mod's crates.</summary>
    private static IReadOnlyList<ISkyrimMod> CreateOverridingWorld(string dataFolder)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var baseGame = new FixtureMod(BaseGamePlugin);
        var target = new FixtureMod(FixtureWorld.TargetName, baseGame);
        var above = new FixtureMod(AbovePlugin, baseGame, target);
        var below = new FixtureMod(BelowPlugin, baseGame, target);
        var spaces = FixtureSpaces.CreateIn(baseGame);
        foreach (var mod in new[] { target, above, below }) spaces.AddOverridesTo(mod);

        var crate = target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel);
        target.Interior.Place(OwnCrate, crate, Vector3.Zero);
        above.Interior.PlaceCopyOf(target.Interior.Place(CrateOverriddenAbove, crate, new Vector3(0, RowSpacing, 0)));
        below.Interior.PlaceCopyOf(target.Interior.Place(CrateOverriddenBelow, crate, new Vector3(0, 2 * RowSpacing, 0)));
        return [baseGame.Mod, target.Mod, above.Mod, below.Mod];
    }

    /// <summary>The overriding world, with an unreadable archive for the plugin below the patch and one for no plugin.</summary>
    private static IReadOnlyList<ISkyrimMod> CreateWorldWithUnreadableArchives(string dataFolder)
    {
        var plugins = CreateOverridingWorld(dataFolder);
        File.WriteAllBytes(Path.Combine(dataFolder, BelowPluginArchive), UnreadableArchive);
        File.WriteAllBytes(Path.Combine(dataFolder, ArchiveOutsideTheWorld), UnreadableArchive);
        return plugins;
    }

    /// <summary>Base game, cleaned mod and the other mod above the patch; below it, plugins that each link the cell or the worldspace to a location of their own.</summary>
    private static IReadOnlyList<ISkyrimMod> CreateWorldOverriddenBelow(
        string dataFolder, OverriddenBelow overridden, int pluginsBelow, CrateStandsIn crateStandsIn = CrateStandsIn.ExteriorCell)
    {
        FixtureMeshes.WriteAll(dataFolder);
        var baseGame = new FixtureMod(BaseGamePlugin);
        var target = new FixtureMod(FixtureWorld.TargetName, baseGame);
        var other = new FixtureMod(OtherPlugin, baseGame);
        var spaces = FixtureSpaces.CreateIn(baseGame);
        spaces.AddOverridesTo(target);
        spaces.AddOverridesTo(other);

        var at = new Vector3(CrateCorner, CrateCorner, FixtureSpaces.TerrainHeight);
        var crate = target.AddModeledStatic("T_CrateBase", FixtureMeshes.CrateModel);
        if (crateStandsIn == CrateStandsIn.PersistentCell) PlacePersistentCrate(baseGame, target, crate, at);
        else target.Exterior.Place(ExteriorCrate, crate, at);
        other.Exterior.Place("O_Pad", other.AddModeledStatic("O_PadBase", FixtureMeshes.PadModel), at + new Vector3(PadCentre, 0, 0));
        var below = BelowPluginNames(pluginsBelow).Select(name => OverrideWithOwnLocation(new FixtureMod(name, baseGame), baseGame, overridden));
        return [baseGame.Mod, target.Mod, other.Mod, .. below.Select(mod => mod.Mod)];
    }

    /// <summary>
    /// The worldspace's persistent cell belongs to the base game, as in the game's own worldspaces; the cleaned mod's copy of it
    /// holds the crate, which also carries the Persistent flag the removal goes by.
    /// </summary>
    private static void PlacePersistentCrate(FixtureMod baseGame, FixtureMod target, FormKey crate, Vector3 at)
    {
        var persistentCell = new Cell(baseGame.NextKey(), FixtureMod.Release);
        baseGame.Mod.Worldspaces.Single().TopCell = persistentCell;
        var targetCopy = new Cell(persistentCell.FormKey, FixtureMod.Release);
        target.Mod.Worldspaces.Single().TopCell = targetCopy;
        var placed = new FixtureCell(target, targetCopy).Place(ExteriorCrate, crate, at, persistent: true);
        placed.MajorRecordFlagsRaw |= (int)PlacedObject.DefaultMajorFlag.Persistent;
    }

    private static FixtureMod OverrideWithOwnLocation(FixtureMod plugin, FixtureMod baseGame, OverriddenBelow overridden)
    {
        var location = new Location(plugin.NextKey(), FixtureMod.Release) { EditorID = plugin.Key.Name + "Location" };
        plugin.Mod.Locations.Add(location);
        var baseWorldspace = baseGame.Mod.Worldspaces.Single();
        var worldspace = new Worldspace(baseWorldspace.FormKey, FixtureMod.Release) { EditorID = baseWorldspace.EditorID };
        if (overridden == OverriddenBelow.Worldspace)
        {
            worldspace.Location.SetTo(location.FormKey);
        }
        else
        {
            var baseCell = baseGame.Mod.EnumerateMajorRecords<ICellGetter>().Single(cell => cell.Grid != null);
            var cell = new Cell(baseCell.FormKey, FixtureMod.Release) { EditorID = baseCell.EditorID, Grid = new CellGrid { Point = new P2Int(baseCell.Grid!.Point.X, baseCell.Grid.Point.Y) } };
            cell.Location.SetTo(location.FormKey);
            worldspace.AddCell(cell);
        }
        plugin.Mod.Worldspaces.Add(worldspace);
        return plugin;
    }
}
