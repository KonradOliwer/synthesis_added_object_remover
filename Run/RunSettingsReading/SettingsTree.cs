using System.Reflection;

namespace AddedObjectRemover;

/// <param name="Property">The settings class property that holds the setting.</param>
/// <param name="ScreenName">The name the settings screen shows for it.</param>
/// <param name="ValueType">The type of the value; for a list, the type of its items.</param>
/// <param name="IsGroup">The setting is a group of settings of the schema's own classes.</param>
internal sealed record SettingNode(PropertyInfo Property, string ScreenName, Type ValueType, bool IsGroup);

/// <summary>The settings classes of a schema seen as a tree of groups and settings; the failure message and the config notes walk it alike.</summary>
internal static class SettingsTree
{
    public static IEnumerable<SettingNode> SettingsOf(Type group, SettingsSchema schema) =>
        group.GetProperties().Select(property =>
        {
            var valueType = ElementTypeOrSelf(property.PropertyType);
            return new SettingNode(property, ScreenNameOf(property, schema), valueType, valueType.IsClass && valueType.Assembly == schema.Root.Assembly);
        });

    public static string ScreenNameOf(PropertyInfo property, SettingsSchema schema) => schema.DisplayNameOf(property) ?? property.Name;

    /// <summary>The item type of a list setting (its elements are what a saved value replaces), or the type itself.</summary>
    public static Type ElementTypeOrSelf(Type type)
    {
        if (type == typeof(string)) return type;
        var enumerable = type.GetInterfaces()
            .Prepend(type)
            .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments()[0] ?? type;
    }
}
