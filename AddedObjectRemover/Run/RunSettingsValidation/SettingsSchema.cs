using System.Reflection;

namespace AddedObjectRemover;

/// <summary>Where a settings.json path leads: the settings class to walk, and the display name a setting is shown under (null when it has none).</summary>
internal sealed record SettingsSchema(Type Root, Func<PropertyInfo, string?> DisplayNameOf);
