using System.Reflection;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The tools hold generic operations only. Every named value in a tool (a constant or a static read-only field of a
/// number, text, key, enum or struct type, or an array of those) is listed in Tools/ToolConstants.txt with its kind, so
/// a game convention cannot slip in unnoticed; and no tool method has an optional parameter, because a rule is passed
/// in by the caller. Inline literals in a tool are covered only by the magic-number rule and by review.
/// </summary>
public class ToolConstantsTests
{
    private const string ListFile = "Tools/ToolConstants.txt";
    private const string FieldSeparator = " | ";

    /// <summary>
    /// math: a fact of arithmetic or geometry. file format / game format: how files and records are encoded.
    /// budget: a performance or robustness limit. internal: a code or text the tool uses only inside itself.
    /// A game convention (a naming habit, a rule about what the game's objects mean) is none of these.
    /// </summary>
    private static readonly string[] Kinds = ["math", "file format", "game format", "budget", "internal"];

    private static readonly BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [Fact]
    public void EveryNamedValueInAToolIsListedWithItsKind()
    {
        var found = NamedValues(ToolTypes()).Select(field => Key(field)).ToHashSet();
        var listed = ReadList();

        var unlisted = found.Except(listed.Keys).Order(StringComparer.Ordinal).ToList();
        var stale = listed.Keys.Except(found).Order(StringComparer.Ordinal).ToList();

        Assert.True(unlisted.Count == 0, $"Not in {ListFile} (a game convention moves to the business caller; anything else gets a kind and a reason):{Environment.NewLine}{string.Join(Environment.NewLine, unlisted)}");
        Assert.True(stale.Count == 0, $"Listed in {ListFile} but no longer there (remove):{Environment.NewLine}{string.Join(Environment.NewLine, stale)}");
    }

    [Fact]
    public void NoToolMethodOrConstructorHasAnOptionalParameter()
    {
        var offenders = OptionalParameters(ToolTypes()).Order(StringComparer.Ordinal).ToList();

        Assert.True(offenders.Count == 0, $"Make required, the caller passes the value:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void TheToolTypesAreFound()
    {
        var names = ToolTypes().Select(BusinessCode.PathName).ToHashSet();

        Assert.Contains("ExteriorGrid", names);
        Assert.Contains("ParallelMap", names);
        Assert.Contains("MeshTriangleTree", names);
    }

    [Fact]
    public void TheNamedValueCheckSeesConstantsReadOnlyFieldsAndArraysButNotObjects()
    {
        var keys = NamedValues([typeof(SampleConstants)]).Select(field => field.Name).Order(StringComparer.Ordinal);

        Assert.Equal(["Axes", "Count", "Limit", "Name", "Origin", "Weekday"], keys);
    }

    [Fact]
    public void TheOptionalParameterCheckSeesMethodsAndConstructors()
    {
        var offenders = OptionalParameters([typeof(SampleOptionals)]).Order(StringComparer.Ordinal);

        Assert.Equal(
            ["SampleOptionals.Method(flag)", "SampleOptionals.Method(key)", "SampleOptionals.Method(number)", "SampleOptionals.SampleOptionals(text)"],
            offenders);
    }

    private static IEnumerable<string> OptionalParameters(IEnumerable<Type> types) =>
        types.SelectMany(type => type.GetMethods(AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(AllDeclared))
            .Where(method => !method.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false))
            .SelectMany(method => method.GetParameters().Where(parameter => parameter.IsOptional)
                .Select(parameter => $"{BusinessCode.PathName(type)}.{(method.IsConstructor ? BusinessCode.NameWithoutArity(type) : method.Name)}({parameter.Name})")));

    private static IEnumerable<FieldInfo> NamedValues(IEnumerable<Type> types) =>
        types.SelectMany(type => type.GetFields(AllDeclared))
            .Where(field => (field.IsLiteral || (field.IsStatic && field.IsInitOnly))
                && !field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false)
                && !field.DeclaringType!.IsEnum
                && IsPlainValue(field.FieldType.IsArray ? field.FieldType.GetElementType()! : field.FieldType));

    private static bool IsPlainValue(Type type) => type.IsPrimitive || type.IsEnum || type.IsValueType || type == typeof(string);

    private static string Key(FieldInfo field) => $"{BusinessCode.PathName(field.DeclaringType!)}.{field.Name}";

    /// <summary>The types declared in the files under Tools, nested ones included.</summary>
    private static IReadOnlyList<Type> ToolTypes()
    {
        var toolNames = SourceFile.All()
            .Where(file => file.IsIn(["Tools"]))
            .SelectMany(file => SourceFile.DeclaredTypes(file.Code))
            .ToHashSet();

        return ProductionSources.Assemblies().SelectMany(assembly => assembly.GetTypes())
            .Where(type => !BusinessCode.IsCompilerGenerated(type) && toolNames.Contains(OutermostName(type)))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
    }

    private static string OutermostName(Type type) =>
        type.DeclaringType is { } outer ? OutermostName(outer) : BusinessCode.NameWithoutArity(type);

    private static Dictionary<string, string> ReadList()
    {
        var path = Path.Combine(BusinessCode.ProductionFolder(), ListFile);
        var entries = new Dictionary<string, string>();
        foreach (var line in File.ReadAllLines(path).Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')))
        {
            var parts = line.Split(FieldSeparator, 3, StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || !Kinds.Contains(parts[1]) || parts[2].Length == 0)
            {
                throw new InvalidDataException($"{path}: '{line}' needs the form 'Type.Field{FieldSeparator}kind{FieldSeparator}reason' with a kind of {string.Join(", ", Kinds)}.");
            }
            entries[parts[0]] = parts[1];
        }
        return entries;
    }
}

internal static class SampleConstants
{
    public const int Count = 3;
    public const string Name = "x";
    public const DayOfWeek Weekday = DayOfWeek.Monday;
    public static readonly float Limit = 1f;
    public static readonly int[] Axes = [0, 1];
    public static readonly System.Numerics.Vector3 Origin = System.Numerics.Vector3.Zero;
    public static readonly object Gate = new();
    public static readonly List<int> Items = [];
    public static int Mutable = 1;
}

internal sealed class SampleOptionals
{
    public SampleOptionals(string text = "") => _ = text;

    public void Method(int number = 1, RecordKey? key = null, bool flag = false) => _ = (number, key, flag);

    public void Required(int number) => _ = number;
}
