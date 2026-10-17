using Newtonsoft.Json;

namespace AddedObjectRemover;

/// <summary>A settings file holds a value that cannot be read; the message names the setting, the value found and what is allowed.</summary>
public sealed class SettingsUnreadableException(string message, Exception inner) : InvalidOperationException(message, inner);

public static class SettingsLoading
{
    /// <summary>Settings are read lazily from the settings file; an invalid saved value throws on first access, and is explained in the exception.</summary>
    /// <param name="fileName">The file the message names for the setting's path.</param>
    public static T Load<T>(Func<T> read, SettingsSchema schema, string fileName) =>
        Load(read, schema, fileName, SettingsFailureMessage.SavedSettingLead);

    /// <summary>Like <see cref="Load{T}(Func{T}, SettingsSchema, string)"/>, for a config file: the message names the file by its full path.</summary>
    internal static T LoadConfigFile<T>(Func<T> read, SettingsSchema schema, string filePath) =>
        Load(read, schema, Path.GetFileName(filePath), $"Invalid setting in config file {filePath}");

    private static T Load<T>(Func<T> read, SettingsSchema schema, string fileName, string lead)
    {
        try
        {
            return read();
        }
        catch (JsonException ex)
        {
            throw new SettingsUnreadableException(SettingsFailureMessage.Describe(ex, schema, fileName, lead), ex);
        }
        catch (ArgumentException ex) when (SettingsFailureMessage.IsUnreadablePluginName(ex, schema))
        {
            throw new SettingsUnreadableException(SettingsFailureMessage.DescribeUnreadablePluginName(ex, schema, lead), ex);
        }
    }
}
