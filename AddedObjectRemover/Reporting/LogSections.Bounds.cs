using System.Collections.Immutable;

namespace AddedObjectRemover;

internal static partial class LogSections
{
    /// <summary>Per built world-bounds index, how long it took: of the rivals' spaces, and of the spaces of every plugin's objects (support and obstacles).</summary>
    public static LogSection BoundsIndexTimes(PhaseTimes times, ReportContext context)
    {
        static string DescribeBuilds(ImmutableArray<TimeSpan> buildTimes) =>
            $"{buildTimes.Length:N0} spaces in {Describe.Seconds(buildTimes.Aggregate(TimeSpan.Zero, (total, time) => total + time))}";

        return new LogSection(
            "bounds-index-times",
            context.Detailed
                ?
                [
                    $"World-bounds indexes built: other mods' objects {DescribeBuilds(times.Samples(TimedPhase.RivalBoundsBuild))}, "
                    + $"supporters and obstacles {DescribeBuilds(times.Samples(TimedPhase.SolidBoundsBuild))}.",
                ]
                : []);
    }

    /// <remarks>The mesh failures are the one part the normal log shows, without the indent that nests them under the statistics line.</remarks>
    public static LogSection Bounds(BoundsStats stats, ReportContext context)
    {
        List<string> lines = [];
        if (context.Detailed)
        {
            lines.Add(
                $"Bounds: {stats.BasesFromNif:N0} bases from NIF, {stats.BasesNifFallbackToObnd:N0} NIF misses, "
                + $"{stats.BasesFromObnd:N0} from OBND, {stats.BasesWithoutBounds + stats.BasesUnresolved:N0} without bounds. "
                + $"Meshes: {stats.ModelsRead:N0} read, {stats.ModelsEffectOnly:N0} effect-only, {stats.ModelsFailed:N0} failed "
                + $"({stats.ModelsFromLooseFiles:N0} loose, {stats.ModelsFromArchives:N0} from {stats.ArchivesIndexed:N0} archives), "
                + $"{stats.ModelsWithFooterRoot:N0} with a footer root other than their first node.");
        }
        if (stats.ModelFailuresByKind.Count > 0)
        {
            var indent = context.Detailed ? "  " : string.Empty;
            lines.Add($"{indent}Mesh failures: {string.Join(", ", stats.ModelFailuresByKind.Select(kv => $"{kv.Value:N0} {kv.Key}"))}.");
        }
        return new LogSection("bounds", [.. lines]);
    }

    public static LogSection Spaces(World world, IReadOnlyList<Removal> removals, ReportContext context)
    {
        if (!context.Detailed) return new LogSection("spaces", []);

        var removedBySpace = removals
            .GroupBy(removal => world.Targets[removal.TargetIndex].SpaceKey)
            .ToDictionary(group => group.Key, group => group.Count());
        var rivalsBySpace = world.Rivals.CountBy(rival => rival.SpaceKey).ToDictionary();
        List<string> lines = ["Per cell/worldspace (target objects / other objects / removed):"];
        foreach (var group in world.Targets
                     .GroupBy(target => target.SpaceKey)
                     .OrderBy(group => world.SpaceNames[group.Key], StringComparer.OrdinalIgnoreCase))
        {
            removedBySpace.TryGetValue(group.Key, out var removedCount);
            lines.Add($"  {world.SpaceNames[group.Key]}: {group.Count():N0} / {rivalsBySpace.GetValueOrDefault(group.Key):N0} / {removedCount:N0}");
        }
        return new LogSection("spaces", [.. lines]);
    }
}
