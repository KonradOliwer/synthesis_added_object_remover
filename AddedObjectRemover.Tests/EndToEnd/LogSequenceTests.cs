using System.Text.RegularExpressions;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Pins every printed log section, empty ones included, in order and with its lines, for every combination of the
/// switches that decide which sections print: also remove, leftover invisible objects, moving kept markers, report
/// files and the detailed log.
/// </summary>
public class LogSequenceTests
{
    private const int Workers = 2;
    private const string ExpectedOutputSubfolder = "log-sequence";

    public static TheoryData<FollowUpRemovalMode, Leftovers, bool, bool> Switches()
    {
        var data = new TheoryData<FollowUpRemovalMode, Leftovers, bool, bool>();
        foreach (var mode in Enum.GetValues<FollowUpRemovalMode>())
        foreach (var leftovers in Enum.GetValues<Leftovers>())
        foreach (var writeReportFiles in (bool[])[false, true])
        foreach (var detailedLog in (bool[])[false, true])
        {
            data.Add(mode, leftovers, writeReportFiles, detailedLog);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Switches))]
    public void PrintedSectionsMatchTheExpectedOutput(FollowUpRemovalMode mode, Leftovers leftovers, bool writeReportFiles, bool detailedLog)
    {
        var log = new RecordingLogSink();

        FixtureRun.Run(Create(mode, leftovers, writeReportFiles, detailedLog), Workers, log, DefaultMeshFiles);

        var name = $"{ExpectedOutputSubfolder}/{mode}-{leftovers}-reports-{writeReportFiles}-detailed-{detailedLog}";
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

    private static StandardSettings Create(FollowUpRemovalMode mode, Leftovers leftovers, bool writeReportFiles, bool detailedLog) => new()
    {
        TargetPlugin = ModKey.FromNameAndExtension(FixtureWorld.TargetPlugin),
        TooClose = new TooCloseRemovalSettings { UseRoughBoxes = false, NpcHandling = NpcHandling.OnlyWhenStuckInsideTheObject },
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
        Diagnostics = new DiagnosticsSettings { DetailedLog = detailedLog, WriteReportFiles = writeReportFiles },
    };
}
