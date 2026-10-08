using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace AddedObjectRemover;

/// <summary>
/// Turns a settings.json deserialization failure (an invalid enum text, a value of the wrong JSON type, ...)
/// into a message naming the setting, the value that was found, and what is allowed there.
/// </summary>
internal static class SettingsFailureMessage
{
    private static readonly Regex PathSegment = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
    private static readonly Regex ConvertedValue = new(
        @"Error converting value (.*?) to type|Could not convert string to \w+: (.*?)\.\s*Path",
        RegexOptions.Compiled);

    public static string Describe(JsonException exception, SettingsSchema schema) =>
        Describe(PathOf(exception), LocationOf(exception), exception.Message, schema);

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

    /// <param name="location">Where in settings.json the error lies; named only when no setting can be.</param>
    private static string Describe(string? path, string? location, string exceptionMessage, SettingsSchema schema)
    {
        var setting = string.IsNullOrEmpty(path) ? null : ResolveSetting(path, schema);
        var settingDescription = setting != null
            ? $"'{setting.Value.Breadcrumb}' (settings.json path '{path}')"
            : location != null ? $"the settings file at {location}" : "the settings file";

        var message = $"Invalid saved setting: {settingDescription}.";
        var foundValue = ExtractFoundValue(exceptionMessage);
        if (foundValue != null) message += $" Value found: {foundValue}.";
        var allowed = setting?.LeafType == null ? null : DescribeAllowedValues(setting.Value.LeafType);
        if (allowed != null) message += $" {allowed}";
        return message;
    }

    /// <summary>Walks the settings.json path through the schema's settings class, naming each setting by its display name.</summary>
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
            names.Add(schema.DisplayNameOf(property) ?? property.Name);
            current = property.PropertyType;
            leaf = ElementTypeOrSelf(current);
        }
        return (string.Join(" > ", names), leaf);
    }

    /// <summary>The item type of a list setting (its elements are what a saved value replaces), or the type itself.</summary>
    private static Type ElementTypeOrSelf(Type type)
    {
        if (type == typeof(string)) return type;
        var enumerable = type.GetInterfaces()
            .Prepend(type)
            .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments()[0] ?? type;
    }

    internal static string? ExtractFoundValue(string message)
    {
        var match = ConvertedValue.Match(message);
        if (!match.Success) return null;
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    internal static string DescribeAllowedValues(Type type)
    {
        if (type.IsEnum) return $"Allowed values: {string.Join(", ", Enum.GetNames(type))}.";
        if (type == typeof(bool)) return "Expected value: true or false.";
        if (type == typeof(int) || type == typeof(float) || type == typeof(double)) return "Expected a number.";
        if (type == typeof(string)) return "Expected a text value.";
        return "Expected a different kind of value.";
    }
}
