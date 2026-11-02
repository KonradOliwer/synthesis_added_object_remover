using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover;

/// <summary>Builds the settings of the report files.</summary>
internal static class ReportFileSettingsBuilder
{
    public static ReportFileSettings Create(string? reportsFolder, SettingsSource source) =>
        new(ResolveReportFolder(reportsFolder ?? throw new ArgumentNullException(nameof(reportsFolder)), source));

    /// <summary>A config file's reports go to a subfolder named after the config, so the configs of a batch do not overwrite each other's.</summary>
    private static string ResolveReportFolder(string reportsFolder, SettingsSource source) =>
        source is SettingsSource.ConfigFile file ? Path.Combine(reportsFolder, file.Name) : reportsFolder;
}
