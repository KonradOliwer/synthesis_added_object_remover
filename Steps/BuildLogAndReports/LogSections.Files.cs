using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>
    /// Where the report files went, in the order touch, support, mesh origins, leftovers, removals, moved and fitted objects, hints. The mesh-origin summary
    /// comes from the explanations, so it shows even when its file could not be written. Only written files have lines;
    /// when writing the files was stopped, the not-performed line comes first.
    /// </summary>
    public static LogSection BuildWrittenSection(ReportFilesResult files, RestingObjectsResult alsoRemove, PluginName target, ReportContext context)
    {
        var tables = files.Outcome == StepOutcome.Performed ? files.Written : [];
        List<string> lines = files.Outcome == StepOutcome.StoppedByUnexpectedError ? [BuildNotPerformedLine("Report files")] : [];
        AddTouchLine(lines, tables, context);
        if (WrittenTables.Find(tables, ReportFileNames.SupportFileName) is { } support)
        {
            lines.Add($"Anchoring diagnostics: wrote {TextFormat.Count(support.Rows)} evaluations to {support.Path}.");
        }
        if (context.Detailed && ExplanationEvidence.HasMeshOrigins(alsoRemove))
        {
            var origins = ExplanationEvidence.MeshOrigins(alsoRemove);
            lines.Add(
                $"Mesh origins: {TextFormat.Count(origins.Length)} meshes of {target} objects, {CountedPlace(origins, MeshOriginPlace.NearBottom)}, "
                + $"{CountedPlace(origins, MeshOriginPlace.NearCentre)}, {CountedPlace(origins, MeshOriginPlace.Other)}.");
        }
        if (WrittenTables.Find(tables, ReportFileNames.MeshOriginsFileName) is { } meshOrigins)
        {
            lines.Add($"  Wrote {TextFormat.Count(meshOrigins.Rows)} mesh origins to {meshOrigins.Path}.");
        }
        if (WrittenTables.Find(tables, ReportFileNames.LeftBehindFileName) is { } leftovers)
        {
            lines.Add($"{LeftBehindStepName} diagnostics: wrote {TextFormat.Count(leftovers.Rows)} evaluations to {leftovers.Path}.");
        }
        AddRowsLine(lines, tables, ReportFileNames.RemovalsFileName, "Removals");
        AddRowsLine(lines, tables, ReportFileNames.FitsFileName, "Moved and fitted objects");
        AddRowsLine(lines, tables, ReportFileNames.HintsFileName, "Removed markers and kept objects");
        return new LogSection("written", [.. lines]);
    }

    /// <summary>E.g. "Removals: wrote 3 rows to …", when the file was written.</summary>
    private static void AddRowsLine(List<string> lines, ImmutableArray<WrittenTable> tables, string fileName, string title)
    {
        if (WrittenTables.Find(tables, fileName) is { } table) lines.Add($"{title}: wrote {TextFormat.Count(table.Rows)} rows to {table.Path}.");
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

    private static string CountedPlace(ImmutableArray<MeshOrigin> origins, MeshOriginPlace place) =>
        $"{TextFormat.Count(origins.Count(origin => origin.Class == place))} {Describe.OriginPlace(place)}";

    /// <param name="warnings">One problem per report file that could not be written.</param>
    public static LogSection BuildReportFileWarningsSection(ImmutableArray<ReportFileProblem> warnings) =>
        new("reportFileWarnings", [.. warnings.Select(BuildReportFileProblemLine)]);

    private static string BuildReportFileProblemLine(ReportFileProblem problem)
    {
        var description = problem.Action switch
        {
            ReportFileAction.DeletingEarlierFiles => "deleting earlier diagnostics files",
            ReportFileAction.WritingFile => $"writing {problem.FileName}",
            _ => throw new UnreachableException(),
        };
        return $"  Warning: {description} in {problem.Folder} failed: {problem.ExceptionMessage}";
    }
}
