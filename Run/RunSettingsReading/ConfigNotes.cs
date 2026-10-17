using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AddedObjectRemover;

/// <summary>
/// Compares a config file with the settings classes: the keys it has no setting for, the settings it leaves out (their defaults
/// are used) and the settings it sets to other than the default. Keys match the way the JSON reader matches them, ignoring case.
/// A value is compared as the settings read it (with the converters named on its setting), not re-read from the JSON.
/// </summary>
internal sealed class ConfigNotes(SettingsSchema schema)
{
    private readonly List<string> _unknownKeys = [];
    private readonly List<SettingInFile> _notInFile = [];
    private readonly List<SettingInFile> _changedFromDefault = [];

    /// <param name="read">The settings read from <paramref name="file"/>.</param>
    public static ConfigFileNotes Of(JObject file, object read, SettingsSchema schema)
    {
        var notes = new ConfigNotes(schema);
        notes.NoteGroup(file, read, Activator.CreateInstance(schema.Root)!, keyPrefix: "", namePrefix: "");
        return new ConfigFileNotes([.. notes._unknownKeys], [.. notes._notInFile], [.. notes._changedFromDefault]);
    }

    private void NoteGroup(JObject written, object read, object defaults, string keyPrefix, string namePrefix)
    {
        var settings = SettingsTree.SettingsOf(defaults.GetType(), schema).ToList();
        foreach (var key in written.Properties())
        {
            if (!settings.Any(setting => string.Equals(setting.Property.Name, key.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _unknownKeys.Add(keyPrefix + key.Name);
            }
        }

        foreach (var setting in settings)
        {
            NoteSetting(written, read, defaults, setting, keyPrefix + setting.Property.Name, namePrefix + setting.ScreenName);
        }
    }

    private void NoteSetting(JObject written, object read, object defaults, SettingNode setting, string keyPath, string screenName)
    {
        var value = written.GetValue(setting.Property.Name, StringComparison.OrdinalIgnoreCase);
        if (value == null || (setting.IsGroup && value.Type == JTokenType.Null))
        {
            _notInFile.Add(new SettingInFile(keyPath, screenName, setting.IsGroup, ValueAsWritten: null));
        }
        else if (setting.IsGroup)
        {
            NoteGroup((JObject)value, setting.Property.GetValue(read)!, setting.Property.GetValue(defaults)!, keyPath + ".", screenName + " > ");
        }
        else if (DiffersFromDefault(setting.Property.GetValue(read), setting.Property.GetValue(defaults), setting.Property.PropertyType))
        {
            _changedFromDefault.Add(new SettingInFile(keyPath, screenName, IsGroup: false, value.ToString(Formatting.None)));
        }
    }

    /// <summary>
    /// Values are compared, not text: 32 and 32.0 are the same number, and lists are the same when their items are in the same order.
    /// A null list is an empty one, as the settings treat it.
    /// </summary>
    private static bool DiffersFromDefault(object? readValue, object? defaultValue, Type type) =>
        IsList(type)
            ? !ItemsOf(readValue).SequenceEqual(ItemsOf(defaultValue))
            : !Equals(readValue, defaultValue);

    private static bool IsList(Type type) => type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

    private static IEnumerable<object> ItemsOf(object? list) => list is System.Collections.IEnumerable items ? items.Cast<object>() : [];
}
