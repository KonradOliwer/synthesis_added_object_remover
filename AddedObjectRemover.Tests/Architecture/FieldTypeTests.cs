using System.Collections.Concurrent;
using System.Reflection;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// Data structures that hold run data belong in the tools and the run's caches. A business class keeps no
/// dictionary, set or lazy value in a field (checked exactly); records are values and may hold them.
/// </summary>
public class FieldTypeTests
{
    private static readonly BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly Type[] BannedFieldTypes = [typeof(Dictionary<,>), typeof(ConcurrentDictionary<,>), typeof(HashSet<>), typeof(Lazy<>)];

    private static readonly Dictionary<string, string> Allowed = new()
    {
    };

    [Fact]
    public void NoBusinessClassHoldsADictionarySetOrLazyInAField()
    {
        var found = FieldsOfBannedTypes(BusinessCode.WrittenTypes()).Order(StringComparer.Ordinal).ToList();

        var unlisted = found.Except(Allowed.Keys).ToList();
        var stale = Allowed.Keys.Except(found).Order(StringComparer.Ordinal).ToList();

        Assert.True(unlisted.Count == 0, $"Fields of banned types:{Environment.NewLine}{string.Join(Environment.NewLine, unlisted)}");
        Assert.True(stale.Count == 0, $"Listed but no longer found:{Environment.NewLine}{string.Join(Environment.NewLine, stale)}");
    }

    [Fact]
    public void TheCheckFindsBannedFieldsOfClassesButNotOfRecordsOrOtherTypes()
    {
        var found = FieldsOfBannedTypes([typeof(SampleHolder), typeof(SampleRecord)]).Order(StringComparer.Ordinal);

        Assert.Equal(["SampleHolder.Concurrent", "SampleHolder.Items", "SampleHolder.Seen", "SampleHolder.Value"], found);
    }

    private static IEnumerable<string> FieldsOfBannedTypes(IEnumerable<Type> types) =>
        types.Where(type => type.IsClass && !IsRecord(type))
            .SelectMany(type => type.GetFields(AllDeclared)
                .Where(field => field.FieldType.IsGenericType && BannedFieldTypes.Contains(field.FieldType.GetGenericTypeDefinition()))
                .Select(field => $"{BusinessCode.PathName(type)}.{field.Name}"));

    private static bool IsRecord(Type type) => type.GetMethod("<Clone>$", AllDeclared) is not null;
}

internal sealed class SampleHolder
{
    public readonly Dictionary<int, int> Items = new();
    public readonly ConcurrentDictionary<int, int> Concurrent = new();
    private readonly HashSet<int> Seen = [];
    private readonly Lazy<int> Value = new(() => 1);
    private readonly List<int> Allowed = [];
    private readonly int[] Array = [];

    public int Count => Items.Count + Concurrent.Count + Seen.Count + Value.Value + Allowed.Count + Array.Length;
}

internal sealed record SampleRecord(Dictionary<int, int> Items);
