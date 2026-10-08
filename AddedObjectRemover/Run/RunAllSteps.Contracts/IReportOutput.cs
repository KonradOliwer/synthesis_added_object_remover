using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.Contracts;

/// <summary>Where the run's report files go. The folder comes from the run's own settings, so runs never share one.</summary>
public interface IReportOutput
{
    /// <summary>Deletes the earlier report files in the run's folder, and only there.</summary>
    /// <returns>The warning line when they could not be deleted; null otherwise, and always null when no files are written.</returns>
    string? Prepare(ReportOptions options);

    /// <param name="tables">Rows hold fields already formatted as CSV text.</param>
    ReportFilesResult Write(ReportOptions options, ImmutableArray<CsvTable> tables);
}
