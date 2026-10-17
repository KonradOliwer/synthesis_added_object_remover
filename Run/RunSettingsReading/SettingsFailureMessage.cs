using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace AddedObjectRemover;

/// <summary>
/// Turns a settings file deserialization failure (an invalid enum text, a value of the wrong JSON type, ...)
/// into a message naming the setting, the value that was found, and what is allowed there.
/// </summary>
internal static class SettingsFailureMessage
{
    private static readonly Regex PathSegment = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
    private static readonly Regex ConvertedValue = new(
        @"Error converting value (.*?) to type|Could not convert string to \w+: (.*?)\.\s*Path|Could not construct ModKey: (.*?)(?: \(Parameter '|[\r\n]|$)",
        RegexOptions.Compiled);

    internal const string SavedSettingLead = "Invalid saved setting";

    /// <param name="lead">How the message opens; it says which file holds the setting when that is not settings.json.</param>
    public static string Describe(JsonException exception, SettingsSchema schema, string fileName, string lead = SavedSettingLead) =>
        Describe(PathOf(exception), LocationOf(exception), exception.Message, schema, fileName, lead);

    /// <summary>The plugin type's own parse failure, thrown while a plugin name is read; it carries no path, so the settings holding plugin names are listed.</summary>
    public static bool IsUnreadablePluginName(ArgumentException exception, SettingsSchema schema) =>
        exception.TargetSite?.DeclaringType == schema.PluginType;

    public static string DescribeUnreadablePluginName(ArgumentException exception, SettingsSchema schema, string lead = SavedSettingLead)
    {
        var settings = string.Join(" or ", PluginSettingNames(schema.Root, schema).Select(name => $"'{name}'"));
        return Compose(lead, $"a plugin name in {settings}", ExtractFoundValue(exception.Message), DescribeAllowedValues(schema.PluginType, schema));
    }

    private static IEnumerable<string> PluginSettingNames(Type group, SettingsSchema schema)
    {
        foreach (var setting in SettingsTree.SettingsOf(group, schema))
        {
            if (setting.ValueType == schema.PluginType)
            {
                yield return setting.ScreenName;
            }
            else if (setting.IsGroup)
            {
                foreach (var inner in PluginSettingNames(setting.ValueType, schema)) yield return $"{setting.ScreenName} > {inner}";
            }
        }
    }

    /// <summary>Path is on <see cref="JsonSerializationException"/> and <see cref="JsonReaderException"/> individually, not on the shared base type.</summary>
    private static string? PathOf(JsonException exception) => exception switch
    {
        JsonSerializationException serialization => serialization.Path,
        JsonReaderException reader => reader.Path,
        _ => null,
    };

    /// <summary>Like the path, the line and position are on the two exception types individually; line 0 means unknown.</summary>
    private static string? LocationOf(JsonException exception) => exception switch
    {
        JsonSerializationException { LineNumber: > 0 } serialization => DescribeLocation(serialization.LineNumber, serialization.LinePosition),
        JsonReaderException { LineNumber: > 0 } reader => DescribeLocation(reader.LineNumber, reader.LinePosition),
        _ => null,
    };

    private static string DescribeLocation(int line, int position) => $"line {line}, position {position}";

    /// <param name="location">Where in the settings file the error lies; named only when no setting can be.</param>
    private static string Describe(string? path, string? location, string exceptionMessage, SettingsSchema schema, string fileName, string lead)
    {
        var setting = string.IsNullOrEmpty(path) ? null : ResolveSetting(path, schema);
        var settingDescription = setting != null
            ? $"'{setting.Value.Breadcrumb}' ({fileName} path '{path}')"
            : location != null ? $"the settings file at {location}" : "the settings file";

        var allowed = setting?.LeafType == null ? null : DescribeAllowedValues(setting.Value.LeafType, schema);
        return Compose(lead, settingDescription, ExtractFoundValue(exceptionMessage), allowed);
    }

    private static string Compose(string lead, string settingDescription, string? foundValue, string? allowed)
    {
        var message = $"{lead}: {settingDescription}.";
        if (foundValue != null) message += $" Value found: {foundValue}.";
        if (allowed != null) message += $" {allowed}";
        return message;
    }

    /// <summary>Walks the settings file path through the schema's settings class, naming each setting by its display name.</summary>
    internal static (string Breadcrumb, Type? LeafType)? ResolveSetting(string jsonPath, SettingsSchema schema)
    {
        var segments = PathSegment.Matches(jsonPath);
        if (segments.Count == 0) return null;

        var names = new List<string>();
        Type current = schema.Root;
        Type? leaf = null;
        foreach (Match match in segments)
        {
            var property = current.GetProperty(match.Value);
            if (property == null)
            {
                names.Add(match.Value);
                leaf = null;
                current = typeof(object);
                continue;
            }
            names.Add(SettingsTree.ScreenNameOf(property, schema));
            current = property.PropertyType;
            leaf = SettingsTree.ElementTypeOrSelf(current);
        }
        return (string.Join(" > ", names), leaf);
    }

    internal static string? ExtractFoundValue(string message)
    {
        var match = ConvertedValue.Match(message);
        if (!match.Success) return null;
        return match.Groups.Cast<Group>().Skip(1).First(group => group.Success).Value.Trim();
    }

    internal static string DescribeAllowedValues(Type type, SettingsSchema schema)
    {
        if (type.IsEnum) return $"Allowed values: {string.Join(", ", Enum.GetNames(type))}.";
        if (type == schema.PluginType) return "Expected a plugin file name, for example SomeMod.esp.";
        if (type == typeof(bool)) return "Expected value: true or false.";
        if (type == typeof(int) || type == typeof(float) || type == typeof(double)) return "Expected a number.";
        if (type == typeof(string)) return "Expected a text value.";
        return "Expected a different kind of value.";
    }
}
