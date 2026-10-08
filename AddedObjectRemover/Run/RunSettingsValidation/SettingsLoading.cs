using Newtonsoft.Json;

namespace AddedObjectRemover;

/// <summary>settings.json holds a value that cannot be read; the message names the setting, the value found and what is allowed.</summary>
internal sealed class SettingsUnreadableException(string message, Exception inner) : InvalidOperationException(message, inner);

internal static class SettingsLoading
{
    /// <summary>Settings are read lazily from settings.json; an invalid saved value throws on first access, and is explained in the exception.</summary>
    public static T Load<T>(Func<T> read, SettingsSchema schema)
    {
        try
        {
            return read();
        }
        catch (JsonException ex)
        {
            throw new SettingsUnreadableException(SettingsFailureMessage.Describe(ex, schema), ex);
        }
    }
}
