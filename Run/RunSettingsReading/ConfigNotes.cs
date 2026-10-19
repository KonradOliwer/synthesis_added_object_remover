using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AddedObjectRemover;

/// <summary>
/// Compares a config file with the settings classes: the keys it has no setting for, the settings it leaves out (their defaults
/// are used) and the settings it sets to other than the default. Keys match the way the JSON reader matches them, ignoring case.
/// A value is compared as the settings read it (with the converters named on its setting), not re-read from the JSON.
/// </summary>
internal sealed class ConfigNotes(SettingsSchema schema, JsonSerializerSettings json)
{
    private readonly List<string> _unknownKeys = [];
    private readonly List<SettingInFile> _notInFile = [];
    private readonly List<SettingInFile> _changedFromDefault = [];

    /// <param name="read">The settings read from <paramref name="file"/>.</param>
    /// <param name="json">The serializer settings the file was read with; the defaults are written with them.</param>
    public static ConfigFileNotes Of(JObject file, object read, SettingsSchema schema, JsonSerializerSettings json)
    {
        var notes = new ConfigNotes(schema, json);
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
        var defaultValue = setting.Property.GetValue(defaults)!;
        if (value == null || (setting.IsGroup && value.Type == JTokenType.Null))
        {
            NoteDefaulted(setting, defaultValue, keyPath, screenName);
        }
        else if (setting.IsGroup)
        {
            NoteGroup((JObject)value, setting.Property.GetValue(read)!, defaultValue, keyPath + ".", screenName + " > ");
        }
        else if (DiffersFromDefault(setting.Property.GetValue(read), defaultValue, setting.Property.PropertyType))
        {
            _changedFromDefault.Add(new SettingInFile(keyPath, screenName, value.ToString(Formatting.None)));
        }
    }

    /// <summary>A left-out group is noted as the settings in it, each with the default it gets.</summary>
    private void NoteDefaulted(SettingNode setting, object defaultValue, string keyPath, string screenName)
    {
        if (!setting.IsGroup)
        {
            _notInFile.Add(new SettingInFile(keyPath, screenName, DefaultAsJson(defaultValue)));
            return;
        }

        foreach (var inner in SettingsTree.SettingsOf(defaultValue.GetType(), schema))
        {
            NoteDefaulted(inner, inner.Property.GetValue(defaultValue)!, keyPath + "." + inner.Property.Name, screenName + " > " + inner.ScreenName);
        }
    }

    /// <summary>The default as the config file would hold it; an unset plugin is null.</summary>
    private string DefaultAsJson(object defaultValue) =>
        defaultValue.GetType() == schema.PluginType && defaultValue.Equals(Activator.CreateInstance(schema.PluginType))
            ? "null"
            : JsonConvert.SerializeObject(defaultValue, Formatting.None, json);

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
