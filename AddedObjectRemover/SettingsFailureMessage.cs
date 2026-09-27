using System.Reflection;
using System.Text.RegularExpressions;
using Mutagen.Bethesda.Synthesis.Settings;
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

    public static string Describe(JsonException exception) => Describe(PathOf(exception), exception.Message);

    /// <summary>Path is on <see cref="JsonSerializationException"/> and <see cref="JsonReaderException"/> individually, not on the shared base type.</summary>
    private static string? PathOf(JsonException exception) => exception switch
    {
        JsonSerializationException serialization => serialization.Path,
        JsonReaderException reader => reader.Path,
        _ => null,
    };

    internal static string Describe(string? path, string exceptionMessage)
    {
        var setting = string.IsNullOrEmpty(path) ? null : ResolveSetting(path);
        var settingDescription = setting == null
            ? "the settings file"
            : $"'{setting.Value.Breadcrumb}' (settings.json path '{path}')";

        var message = $"Invalid saved setting: {settingDescription}.";
        var foundValue = ExtractFoundValue(exceptionMessage);
        if (foundValue != null) message += $" Value found: {foundValue}.";
        var allowed = setting?.LeafType == null ? null : DescribeAllowedValues(setting.Value.LeafType);
        if (allowed != null) message += $" {allowed}";
        return message;
    }

    /// <summary>Walks the settings.json path through the <see cref="Settings"/> type, following <see cref="SynthesisSettingName"/> attributes.</summary>
    internal static (string Breadcrumb, Type? LeafType)? ResolveSetting(string jsonPath)
    {
        var segments = PathSegment.Matches(jsonPath);
        if (segments.Count == 0) return null;

        var names = new List<string>();
        Type current = typeof(Settings);
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
            var displayName = property.GetCustomAttribute<SynthesisSettingName>()?.Name;
            names.Add(displayName ?? property.Name);
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
        return $"Expected type: {type.Name}.";
    }
}
