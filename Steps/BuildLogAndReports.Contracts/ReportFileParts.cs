namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>The part of the report step that records its unexpected errors, in the report tables and the report files; it has no subject.</summary>
public static class ReportFileParts
{
    private const string Owner = "BuildLogAndReports";

    /// <summary>Building and writing the report files.</summary>
    public static readonly PartKey ReportFiles = new(Owner, nameof(ReportFiles));
}
