using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <remarks>The mesh failures are the one part the normal log shows, without the indent that nests them under the statistics line.</remarks>
    /// <param name="triangles">The meshes whose triangles could not be read; objects using them have no mesh triangles for the rest of the run.</param>
    public static LogSection BuildBoundsSection(BoundsStats stats, TriangleFailureCounts triangles, ReportContext context)
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
        var indent = context.Detailed ? "  " : string.Empty;
        if (stats.ModelFailuresByKind.Count > 0) lines.Add($"{indent}Mesh failures: {MeshFailureCounts(stats.ModelFailuresByKind)}.");
        if (triangles.ByKind.Count > 0) lines.Add($"{indent}Mesh triangle failures: {MeshFailureCounts(triangles.ByKind)}.");
        if (triangles.AfterGoodRead > 0)
        {
            lines.Add($"{indent}Meshes whose triangles failed after reading fine earlier in the run: {TextFormat.Count(triangles.AfterGoodRead)}.");
        }
        return new LogSection("bounds", [.. lines]);
    }

    private static string MeshFailureCounts(IEnumerable<KeyValuePair<MeshFailureCategory, int>> failures)
    {
        var worded = failures.Select(entry => KeyValuePair.Create(MeshProblemText.FailureCategory(entry.Key), entry.Value));
        return string.Join(Describe.ListSeparator, TextLists.Counts(KeyedGroups.Rank(worded, StringComparer.Ordinal)));
    }

    public static LogSection BuildSpacesSection(CollectedObjects world, IReadOnlyList<RemovedObject> removals, ReportContext context)
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
