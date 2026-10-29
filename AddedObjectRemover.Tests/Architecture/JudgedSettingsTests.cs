using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// Groups of objects are merged when their settings are equal. Record equality compares every instance field, public or private,
/// by value for a simple value, a string and a record of them, but by reference for a list, an array or a class. So a settings
/// record used as a group's settings holds only such fields: a list would never merge, and the run would silently gain one more pass.
/// </summary>
public class JudgedSettingsTests
{
    private static readonly Type[] SimpleValueTypes = [typeof(bool), typeof(int), typeof(float), typeof(double), typeof(string)];

    private static readonly Regex BackingFieldName = new(@"^<(?<property>\w+)>k__BackingField$");

    [Fact]
    public void TheSettingsRecordsOfTheGroupsHoldOnlyFieldsComparedByValue()
    {
        var found = GroupSettingsTypes().SelectMany(FindFieldsOfOtherKinds).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            found.Count == 0,
            $"Settings of judged groups with a field that is not compared by value:{Environment.NewLine}{string.Join(Environment.NewLine, found)}");
    }

    [Fact]
    public void TheGroupSettingsAreTheThreeStepsRecords()
    {
        Assert.Equal(
            ["ChangedTerrainSettings", "FitSettings", "TooCloseSettings"],
            GroupSettingsTypes().Select(type => type.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheFieldCheckAcceptsSimpleValuesEnumsStringsAndRecordsOfThemButNotListsArraysOrClasses()
    {
        Assert.Equal(
            ["WithOtherKinds.Class: SampleClass", "WithOtherKinds.List: ImmutableArray`1", "WithOtherKinds.Nested: WithListInside", "WithOtherKinds.Values: Int32[]"],
            FindFieldsOfOtherKinds(typeof(WithOtherKinds)).Order(StringComparer.Ordinal));
        Assert.Empty(FindFieldsOfOtherKinds(typeof(WithSimpleValues)));
    }

    private static IEnumerable<Type> GroupSettingsTypes() =>
        typeof(CollisionGroup).GetProperties()
            .Select(property => property.PropertyType)
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ObjectsJudgedWith<>))
            .Select(type => type.GetGenericArguments().Single());

    /// <returns>"Type.Field: FieldType" for each instance field that is not a simple value, an enum or a record of them; a property's backing field is named by the property.</returns>
    private static IEnumerable<string> FindFieldsOfOtherKinds(Type record) =>
        record.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(field => !IsComparedByValue(field.FieldType))
            .Select(field => $"{record.Name}.{NameOf(field)}: {field.FieldType.Name}");

    private static string NameOf(FieldInfo field) =>
        BackingFieldName.Match(field.Name) is { Success: true } backing ? backing.Groups["property"].Value : field.Name;

    private static bool IsComparedByValue(Type type) =>
        SimpleValueTypes.Contains(type) || type.IsEnum || (IsRecord(type) && !FindFieldsOfOtherKinds(type).Any());

    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null;

    private enum Kind
    {
        One,
    }

    private sealed record Inner(int Value);

    private sealed record WithSimpleValues(bool Flag, int Count, float Size, double Wide, string Name, Kind Kind, Inner Nested);

    private sealed class SampleClass;

    private sealed record WithListInside(ImmutableArray<int> Values);

    private sealed record WithOtherKinds(ImmutableArray<int> List, SampleClass Class, WithListInside Nested, int Fine)
    {
        public int[] Values = [];
    }
}
