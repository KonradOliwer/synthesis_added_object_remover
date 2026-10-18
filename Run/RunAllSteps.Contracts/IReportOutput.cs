using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.Contracts;

/// <summary>Where the run's report files go. The folder comes from the run's own settings, so runs never share one.</summary>
public interface IReportOutput
{
    /// <summary>Deletes the earlier report files in the run's folder, and only there.</summary>
    /// <param name="settings">Null when the run writes no report files.</param>
    /// <returns>Whether report files are written, which this writer alone decides, and the problem when the earlier files could not be deleted.</returns>
    ReportFolderPrepared Prepare(ReportFileSettings? settings);

    /// <param name="settings">Null when the run writes no report files.</param>
    /// <param name="tables">Rows hold fields already formatted as CSV text. Only performed tables are written, in the folder of the settings; otherwise the result carries the tables' outcome and writes nothing.</param>
    /// <returns>A failure that stops the writing as a whole is in the result; it is not thrown.</returns>
    ReportFilesResult Write(ReportFileSettings? settings, ReportTables tables);
}
