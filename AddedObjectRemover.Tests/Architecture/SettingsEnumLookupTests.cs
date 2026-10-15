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
