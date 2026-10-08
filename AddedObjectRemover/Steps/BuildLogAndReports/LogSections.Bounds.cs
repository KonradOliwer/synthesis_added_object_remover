using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <remarks>The mesh failures are the one part the normal log shows, without the indent that nests them under the statistics line.</remarks>
    public static LogSection Bounds(BoundsStats stats, ReportContext context)
    {
        List<string> lines = [];
        if (context.Detailed)
        {
            lines.Add(
                $"Bounds: {TextFormat.Count(stats.BasesFromNif)} bases from NIF, {TextFormat.Count(stats.BasesNifFallbackToObnd)} NIF misses, "
                + $"{TextFormat.Count(stats.BasesFromObnd)} from OBND, {TextFormat.Count(stats.BasesWithoutBounds + stats.BasesUnresolved)} without bounds. "
                + $"Meshes: {TextFormat.Count(stats.ModelsRead)} read, {TextFormat.Count(stats.ModelsEffectOnly)} effect-only, {TextFormat.Count(stats.ModelsFailed)} failed "
                + $"({TextFormat.Count(stats.ModelsFromLooseFiles)} loose, {TextFormat.Count(stats.ModelsFromArchives)} from {TextFormat.Count(stats.ArchivesIndexed)} archives), "
                + $"{TextFormat.Count(stats.ModelsWithFooterRoot)} with a footer root other than their first node.");
        }
        if (stats.ModelFailuresByKind.Count > 0)
        {
            var indent = context.Detailed ? "  " : string.Empty;
            lines.Add($"{indent}Mesh failures: {string.Join(Describe.ListSeparator, TextLists.Counts(stats.ModelFailuresByKind))}.");
        }
        return new LogSection("bounds", [.. lines]);
    }

    public static LogSection Spaces(CollectedObjects world, IReadOnlyList<RemovedObject> removals, ReportContext context)
    {
        if (!context.Detailed) return new LogSection("spaces", []);

        var removedBySpace = removals.CountBy(removal => world.Targets[removal.TargetIndex].SpaceKey).ToDictionary();
        var otherModObjectsBySpace = world.OtherModObjects.CountBy(other => other.SpaceKey).ToDictionary();
        List<string> lines = ["Per cell/worldspace (target objects / other objects / removed):"];
        foreach (var group in world.Targets
                     .GroupBy(target => target.SpaceKey)
                     .OrderBy(group => Describe.Space(world, group.Key), StringComparer.OrdinalIgnoreCase))
        {
            removedBySpace.TryGetValue(group.Key, out var removedCount);
            lines.Add($"  {Describe.Space(world, group.Key)}: {TextFormat.Count(group.Count())} / {TextFormat.Count(otherModObjectsBySpace.GetValueOrDefault(group.Key))} / {TextFormat.Count(removedCount)}");
        }
        return new LogSection("spaces", [.. lines]);
    }
}
