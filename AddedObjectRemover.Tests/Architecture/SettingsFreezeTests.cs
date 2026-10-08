using System.Runtime.CompilerServices;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The settings property names are the keys of users' saved settings.json, and the enum names are the values
/// stored in it, so renaming any of them resets what users saved. The class names are not stored, so only the
/// property paths (such as WhatToCheck.ZoneShape) and the enum types with their values are frozen.
/// A deliberate change edits SettingsFreeze.txt.
/// </summary>
public class SettingsFreezeTests
{
    [Fact]
    public void TheSettingsNamesAreUnchanged() =>
        Assert.Equal(ReadFrozenNames(), DescribeSettingsNames());

    private static List<string> DescribeSettingsNames()
    {
        var names = new List<string>();
        AddPropertyPaths(typeof(Settings), prefix: string.Empty, names);
        return [.. names.Distinct().Order(StringComparer.Ordinal)];
    }

    private static void AddPropertyPaths(Type type, string prefix, List<string> names)
    {
        foreach (var property in type.GetProperties())
        {
            var path = prefix + property.Name;
            names.Add(path);
            var valueType = property.PropertyType.IsGenericType ? property.PropertyType.GetGenericArguments().Single() : property.PropertyType;
            if (valueType.IsEnum)
            {
                names.Add(valueType.Name);
                names.AddRange(Enum.GetNames(valueType).Select(value => $"{valueType.Name}.{value}"));
            }
            else if (valueType.Assembly == typeof(Settings).Assembly)
            {
                AddPropertyPaths(valueType, path + ".", names);
            }
        }
    }

    private static string[] ReadFrozenNames([CallerFilePath] string sourceFile = "") =>
        File.ReadAllLines(Path.Combine(Path.GetDirectoryName(sourceFile)!, "SettingsFreeze.txt"));
}
