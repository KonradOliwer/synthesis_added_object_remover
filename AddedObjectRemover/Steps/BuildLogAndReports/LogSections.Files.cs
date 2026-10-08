using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>
    /// Where the report files went, in the order touch, anchoring, mesh origins, leftovers, hints. The mesh-origin summary
    /// comes from the explanations, so it shows even when its file could not be written.
    /// </summary>
    public static LogSection Written(ImmutableArray<WrittenTable> tables, ReportFileDetails explanations, ReportContext context)
    {
        List<string> lines = [];
        AddTouchLine(lines, tables, context);
        if (WrittenTables.Find(tables, ReportFileNames.AnchoringFileName) is { } anchoring)
        {
            lines.Add($"Anchoring diagnostics: wrote {TextFormat.Count(anchoring.Rows)} evaluations to {anchoring.Path}.");
        }
        if (context.Detailed && explanations.MeshOrigins is { } origins)
        {
            lines.Add(
                $"Mesh origins: {TextFormat.Count(origins.Length)} target meshes, {TextFormat.Count(CountOrigins(origins, MeshOriginPlaces.NearBottom))} {MeshOriginPlaces.NearBottom}, "
                + $"{TextFormat.Count(CountOrigins(origins, MeshOriginPlaces.NearCentre))} {MeshOriginPlaces.NearCentre}, "
                + $"{TextFormat.Count(CountOrigins(origins, MeshOriginPlaces.Other))} {MeshOriginPlaces.Other}.");
        }
        if (WrittenTables.Find(tables, ReportFileNames.MeshOriginsFileName) is { } meshOrigins)
        {
            lines.Add($"  Wrote {TextFormat.Count(meshOrigins.Rows)} mesh origins to {meshOrigins.Path}.");
        }
        if (WrittenTables.Find(tables, ReportFileNames.LeftBehindFileName) is { } leftovers)
        {
            lines.Add($"Leftover invisible objects diagnostics: wrote {TextFormat.Count(leftovers.Rows)} evaluations to {leftovers.Path}.");
        }
        if (WrittenTables.Find(tables, ReportFileNames.HintsFileName) is { } hints)
        {
            lines.Add($"Manual patch hints: wrote {TextFormat.Count(hints.Rows)} rows to {hints.Path}.");
        }
        return new LogSection("written", [.. lines]);
    }

    /// <remarks>The edges and components files share one line; it needs both, because a failed file has its own warning.</remarks>
    private static void AddTouchLine(List<string> lines, ImmutableArray<WrittenTable> tables, ReportContext context)
    {
        if (WrittenTables.Find(tables, ReportFileNames.EdgesFileName) is not { } edges
            || WrittenTables.Find(tables, ReportFileNames.ComponentsFileName) is not { } components)
        {
            return;
        }
        lines.Add(
            $"Touch diagnostics: wrote {TextFormat.Count(edges.Rows)} edges, {TextFormat.Count(components.Rows)} components"
            + $"{Describe.TimedSuffix(edges.Elapsed + components.Elapsed, context)} to {edges.Path} / {components.Path}.");
    }

    private static int CountOrigins(ImmutableArray<MeshOrigin> origins, string classification) =>
        origins.Count(origin => origin.Class == classification);

    /// <param name="warnings">One line per report file that could not be written.</param>
    public static LogSection ReportFileWarnings(ImmutableArray<string> warnings) => new("reportFileWarnings", warnings);
}
