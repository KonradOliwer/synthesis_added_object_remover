using System.Collections.Immutable;

namespace AddedObjectRemover;

internal static partial class LogSections
{
    /// <summary>
    /// Where the report files went, in the order touch, anchoring, mesh origins, leftovers, hints. The mesh-origin summary
    /// comes from the explanations, so it shows even when its file could not be written.
    /// </summary>
    public static LogSection Written(ImmutableArray<WrittenTable> tables, Explanations explanations, ReportContext context)
    {
        List<string> lines = [];
        AddTouchLine(lines, tables, context);
        if (Find(tables, Tables.AnchoringFileName) is { } anchoring)
        {
            lines.Add($"Anchoring diagnostics: wrote {anchoring.Rows:N0} evaluations to {anchoring.Path}.");
        }
        if (context.Detailed && explanations.MeshOrigins is { } origins)
        {
            lines.Add(
                $"Mesh origins: {origins.Length:N0} target meshes, {CountOrigins(origins, MeshOriginSurvey.NearBottom):N0} {MeshOriginSurvey.NearBottom}, "
                + $"{CountOrigins(origins, MeshOriginSurvey.NearCentre):N0} {MeshOriginSurvey.NearCentre}, "
                + $"{CountOrigins(origins, MeshOriginSurvey.Other):N0} {MeshOriginSurvey.Other}.");
        }
        if (Find(tables, Tables.MeshOriginsFileName) is { } meshOrigins)
        {
            lines.Add($"  Wrote {meshOrigins.Rows:N0} mesh origins to {meshOrigins.Path}.");
        }
        if (Find(tables, Tables.LeftoversFileName) is { } leftovers)
        {
            lines.Add($"Leftover invisible objects diagnostics: wrote {leftovers.Rows:N0} evaluations to {leftovers.Path}.");
        }
        if (Find(tables, Tables.HintsFileName) is { } hints)
        {
            lines.Add($"Manual patch hints: wrote {hints.Rows:N0} rows to {hints.Path}.");
        }
        return new LogSection("written", [.. lines]);
    }

    /// <remarks>The edges and components files share one line; it needs both, because a failed file has its own warning.</remarks>
    private static void AddTouchLine(List<string> lines, ImmutableArray<WrittenTable> tables, ReportContext context)
    {
        if (Find(tables, Tables.EdgesFileName) is not { } edges
            || Find(tables, Tables.ComponentsFileName) is not { } components)
        {
            return;
        }
        lines.Add(
            $"Touch diagnostics: wrote {edges.Rows:N0} edges, {components.Rows:N0} components"
            + $"{Describe.TimedSuffix(edges.Elapsed + components.Elapsed, context)} to {edges.Path} / {components.Path}.");
    }

    private static int CountOrigins(ImmutableArray<MeshOrigin> origins, string classification) =>
        origins.Count(origin => origin.Class == classification);

    private static WrittenTable? Find(ImmutableArray<WrittenTable> tables, string fileName) =>
        tables.FirstOrDefault(table => table.FileName == fileName);

    /// <param name="warnings">One line per report file that could not be written.</param>
    public static LogSection ReportFileWarnings(ImmutableArray<string> warnings) => new("reportFileWarnings", warnings);
}
