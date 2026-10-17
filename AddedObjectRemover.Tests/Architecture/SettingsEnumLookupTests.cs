namespace AddedObjectRemover.Tests.Architecture;

/// <summary>The Synthesis settings screen finds each enum setting's type with Assembly.GetType on the entry assembly; a type it can't find has no dropdown.</summary>
public class SettingsEnumLookupTests
{
    [Fact]
    public void EveryEnumSettingIsFoundInTheEntryAssemblyByName()
    {
        var missing = EnumSettingTypes(typeof(Settings))
            .Distinct()
            .Where(type => typeof(Settings).Assembly.GetType(type.FullName!) != type)
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>The settings screen shows each dropdown's initial value as the default's ToString(); a name the dropdown doesn't list shows blank.</summary>
    [Fact]
    public void EveryEnumSettingStartsOnAListedName()
    {
        var notListed = EnumDefaults(new Settings())
            .Where(entry => !Enum.GetNames(typeof(Settings).Assembly.GetType(entry.Type.FullName!)!).Contains(entry.Default.ToString()!))
            .Select(entry => $"{entry.Type.Name}.{entry.Default}")
            .ToList();

        Assert.Empty(notListed);
    }

    [Fact]
    public void TheDetailedLogAndTheReportFilesAreOffByDefault()
    {
        var diagnostics = new Settings().Standard.Diagnostics;

        Assert.False(diagnostics.DetailedLog);
        Assert.False(diagnostics.WriteReportFiles);
    }

    private static IEnumerable<(Type Type, object Default)> EnumDefaults(object settings)
    {
        foreach (var property in settings.GetType().GetProperties())
        {
            var value = property.GetValue(settings);
            if (property.PropertyType.IsEnum)
            {
                yield return (property.PropertyType, value!);
            }
            else if (value != null && property.PropertyType.Assembly == typeof(Settings).Assembly)
            {
                foreach (var nested in EnumDefaults(value))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<Type> EnumSettingTypes(Type settingsType)
    {
        foreach (var property in settingsType.GetProperties())
        {
            var valueType = property.PropertyType.IsGenericType ? property.PropertyType.GetGenericArguments().Single() : property.PropertyType;
            if (valueType.IsEnum)
            {
                yield return valueType;
            }
            else if (valueType.Assembly == typeof(Settings).Assembly)
            {
                foreach (var nested in EnumSettingTypes(valueType))
                {
                    yield return nested;
                }
            }
        }
    }
}
