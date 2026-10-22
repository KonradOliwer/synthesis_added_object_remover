namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>The names of the report files; the writer of the report folder needs them to know which earlier files are stale.</summary>
public static class ReportFileNames
{
    public const string EdgesFileName = "edges.csv";
    public const string ComponentsFileName = "components.csv";
    public const string SupportFileName = "anchoring.csv";
    public const string MeshOriginsFileName = "mesh-origins.csv";
    public const string LeftBehindFileName = "leftover-invisible-objects.csv";
    public const string HintsFileName = "removed-markers-and-kept-objects.csv";
    public const string RemovalsFileName = "removals.csv";
}
