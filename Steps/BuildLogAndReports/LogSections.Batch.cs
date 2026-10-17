using System.Collections.Immutable;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>The lines that open one config's run in a batch.</summary>
internal static partial class LogSections
{
    /// <param name="target">The mod the config cleans up, as the file spells it.</param>
    /// <param name="detailed">Whether this config's detailed log is on: only then the values that differ from the default are listed.</param>
    public static ImmutableArray<LogSection> BuildConfigFileSections(SettingsSource.ConfigFile file, PluginName target, bool detailed) =>
    [
        BuildConfigDelimiterSection(file, target),
        BuildIgnoredKeysSection(file),
        BuildDefaultedSettingsSection(file),
        BuildNonDefaultSettingsSection(file, detailed),
    ];

    private static LogSection BuildConfigDelimiterSection(SettingsSource.ConfigFile file, PluginName target) =>
        new("configDelimiter", [$"=== Config {file.Number} of {file.Count}: {file.FileName}, mod to clean up: {target} ==="]);

    private static LogSection BuildIgnoredKeysSection(SettingsSource.ConfigFile file) =>
        new("configIgnoredKeys",
            file.Notes.UnknownKeys.IsEmpty
                ? []
                : [$"Ignored in {file.FileName}, because this patcher has no such setting (check the spelling and the section it is in): "
                    + $"{string.Join(Describe.ListSeparator, file.Notes.UnknownKeys)}."]);

    private static LogSection BuildDefaultedSettingsSection(SettingsSource.ConfigFile file) =>
        new("configDefaulted",
            file.Notes.NotInFile.IsEmpty
                ? []
                : [$"Not in {file.FileName}, so the default is used:", .. file.Notes.NotInFile.Select(DefaultedLine)]);

    private static LogSection BuildNonDefaultSettingsSection(SettingsSource.ConfigFile file, bool detailed) =>
        new("configNonDefault",
            detailed && !file.Notes.ChangedFromDefault.IsEmpty
                ? [$"Set in {file.FileName} to other than the default:", .. file.Notes.ChangedFromDefault.Select(NonDefaultLine)]
                : []);

    private static string DefaultedLine(SettingInFile setting) =>
        $"  {setting.ScreenName} ({setting.KeyPath}){(setting.IsGroup ? ", all settings in it" : string.Empty)}";

    private static string NonDefaultLine(SettingInFile setting) => $"  {setting.ScreenName} ({setting.KeyPath}): {setting.ValueAsWritten}";
}
