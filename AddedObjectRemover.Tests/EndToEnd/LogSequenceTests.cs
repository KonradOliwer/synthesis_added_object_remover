using System.Text.RegularExpressions;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Pins every printed log section, empty ones included, in order and with its lines, for every combination of the
/// switches that decide which sections print: also remove, leftover invisible objects, moving kept markers, and the
/// detailed log and reports.
/// </summary>
public class LogSequenceTests
{
    private const int Workers = 2;
    private const string ExpectedOutputSubfolder = "log-sequence";

    public static TheoryData<FollowUpRemovalMode, Leftovers, bool> Switches()
    {
        var data = new TheoryData<FollowUpRemovalMode, Leftovers, bool>();
        foreach (var mode in Enum.GetValues<FollowUpRemovalMode>())
        foreach (var leftovers in Enum.GetValues<Leftovers>())
        foreach (var detailedLogAndReports in (bool[])[false, true])
        {
            data.Add(mode, leftovers, detailedLogAndReports);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Switches))]
    public void PrintedSectionsMatchTheExpectedOutput(FollowUpRemovalMode mode, Leftovers leftovers, bool detailedLogAndReports)
    {
        var log = new RecordingLogSink();

        FixtureRun.Run(Create(mode, leftovers, detailedLogAndReports), Workers, log, DefaultMeshFiles);

        var name = $"{ExpectedOutputSubfolder}/{mode}-{leftovers}-detailed-log-and-reports-{detailedLogAndReports}";
        ExpectedOutputFiles.AssertMatches(name, Describe(log.Sections));
    }

    /// <summary>Whether leftover invisible objects are removed, and whether kept markers are moved out of other mods' objects.</summary>
    public enum Leftovers
    {
        Kept,
        Removed,
        RemovedAndMarkersMoved,
    }

    private static IMeshFilesFactory DefaultMeshFiles(string dataFolder, Mutagen.Bethesda.GameRelease release, IReadOnlyList<Mutagen.Bethesda.Plugins.ModKey> listedPlugins) =>
        new MeshFilesFactory(dataFolder, release, listedPlugins);

    /// <summary>Each section as its id, then its lines masked as the end-to-end log is.</summary>
    private static List<string> Describe(IReadOnlyList<LogSection> sections)
    {
        var root = sections.SelectMany(section => section.Lines).Select(line => RunFolder().Match(line)).FirstOrDefault(match => match.Success)?.Value;
        return [.. sections.SelectMany(section => (IEnumerable<string>)["## " + section.Id, .. root is null ? section.Lines : OutputMasks.MaskLog(section.Lines, root)])];
    }

    /// <summary>The temporary folder <see cref="FixtureRun"/> runs in.</summary>
    private static Regex RunFolder() => new(Regex.Escape(Path.GetTempPath()) + @"aor-run-[^\\/\s]+", RegexOptions.IgnoreCase);

    private static Settings Create(FollowUpRemovalMode mode, Leftovers leftovers, bool detailedLogAndReports) => new()
    {
        DetailedLogAndReports = detailedLogAndReports,
        Standard = CreateStandard(mode, leftovers),
    };

    private static StandardSettings CreateStandard(FollowUpRemovalMode mode, Leftovers leftovers) => new()
    {
        CleanedMod = ModKey.FromNameAndExtension(FixtureWorld.TargetName),
        TooClose = new TooCloseRemovalSettings
        {
            ByDistance = new TooCloseByDistanceSettings { Use = false },
            UseRoughBoxes = false,
            NpcHandling = NpcHandling.OnlyWhenStuckInsideTheObject,
        },
        TerrainChange = new TerrainChangeSettings { RemoveOnChangedTerrain = false },
        WhatToIgnore = new IgnoreSettings
        {
            ExcludedPlugins = [ModKey.FromNameAndExtension(FixtureWorld.ExcludedPlugin)],
            IgnoreTargetMasters = true,
            CompatibilityPatches = new CompatibilityPatchSettings { IgnoreModsPatchedWithTarget = true },
        },
        FollowUpRemoval = new FollowUpRemovalSettings { Mode = mode },
        LeftoverInvisibleObjects = new LeftoverInvisibleObjectSettings
        {
            RemoveLeftoverInvisibleObjects = leftovers != Leftovers.Kept,
            MoveKeptMarkersOutOfOtherModsObjects = leftovers == Leftovers.RemovedAndMarkersMoved,
        },
    };
}
