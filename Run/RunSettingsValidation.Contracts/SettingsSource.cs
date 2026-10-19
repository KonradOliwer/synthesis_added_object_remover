using System.Collections.Immutable;

namespace AddedObjectRemover.Run.RunSettingsValidation.Contracts;

/// <summary>Where a run's settings come from: the Standard group of settings.json, or one config file of a batch.</summary>
public abstract record SettingsSource
{
    public sealed record Standard : SettingsSource;

    /// <param name="Name">The config's name: its file name without the extension.</param>
    /// <param name="FileName">The config's file name, as the log shows it.</param>
    /// <param name="FilePath">The config's full path, as the failure messages show it.</param>
    /// <param name="Number">The config's place in the group file, counting from 1.</param>
    /// <param name="Count">How many configs the group file lists.</param>
    public sealed record ConfigFile(string Name, string FileName, string FilePath, int Number, int Count, ConfigFileNotes Notes) : SettingsSource;
}

/// <summary>What reading a config file found besides its values.</summary>
/// <param name="UnknownKeys">Key paths in the file that this patcher has no setting for.</param>
/// <param name="NotInFile">Settings the file leaves out (also those of a left-out group), with the default each gets.</param>
/// <param name="ChangedFromDefault">Settings the file sets to other than their default.</param>
public sealed record ConfigFileNotes(
    ImmutableArray<string> UnknownKeys,
    ImmutableArray<SettingInFile> NotInFile,
    ImmutableArray<SettingInFile> ChangedFromDefault);

/// <param name="KeyPath">The JSON path of the setting in the file.</param>
/// <param name="ScreenName">The setting's name on the settings screen, group first.</param>
/// <param name="ValueAsJson">The value as the config file holds it: the file's own, or the default for a setting the file leaves out.</param>
public sealed record SettingInFile(string KeyPath, string ScreenName, string ValueAsJson);
