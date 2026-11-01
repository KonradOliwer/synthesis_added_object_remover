using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// Run state belongs to the run, so nothing static may hold changing data. A static array counts: its elements stay writable
/// by any caller, so fixed content is an <c>ImmutableArray</c> or a frozen set.
/// </summary>
public partial class StaticStateTests
{
    [Fact]
    public void NoStaticFieldOrPropertyCanHoldChangingData()
    {
        var offenders = SourceFile.All()
            .SelectMany(file => MutableStatics(file.Code).Select(name => $"{file.Description}: {name}"))
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void ReassignableAndGrowableStaticsAreFound()
    {
        const string code = """
            private static int _count;
            public static string Name { get; set; }
            private static readonly Dictionary<string, int> Cache = new();
            private static readonly System.Collections.Generic.HashSet<int> Seen = [];
            private static readonly Lazy<int> Value = new(() => 1);
            public static readonly string[] Fields = ["a"];
            private static readonly Vector3[] Directions =
            [
                Vector3.Zero,
            ];
            private static readonly (int Id, string Text)[] Pairs = [];
            private static readonly Func<int, bool>[] Checks = [];
            private static readonly IReadOnlyDictionary<int, int> View = new Dictionary<int, int>();
            private static readonly IReadOnlyList<int> Items = new List<int>
            {
                1,
            };
            public static Holder Empty { get; } = new(new HashSet<int>(), Failure: null);
            """;

        Assert.Equal(["_count", "Name", "Cache", "Seen", "Value", "Fields", "Directions", "Pairs", "Checks", "View", "Items", "Empty"], MutableStatics(code));
    }

    [Fact]
    public void ConstantStaticsAreNotFound()
    {
        const string code = """
            private static readonly object Lock = new();
            private static readonly ImmutableHashSet<int> Kinds = [1];
            public static readonly ImmutableArray<string> Fields = ["a"];
            private static readonly ImmutableArray<(int Id, string Text)> Pairs = [];
            private static readonly SearchValues<char> Quoted = SearchValues.Create(",");
            public static Foo None { get; } = new();
            public static int Next() => 1;
            public static class Holder { }
            public static abstract TSelf Zero { get; }
            public static bool operator ==(Foo a, Foo b) => true;
            private static readonly IReadOnlyDictionary<int, int> Frozen = new Dictionary<int, int>
            {
                [1] = 2,
            }.ToFrozenDictionary();
            public static Holder Empty { get; } = new(ImmutableDictionary<int, int>.Empty, Failure: null);
            """;

        Assert.Empty(MutableStatics(code));
    }

    private static List<string> MutableStatics(string code) =>
        StaticMemberPattern().Matches(code)
            .Where(member => IsMutable(member) || HoldsNewGrowableCollection(Initializer(code, member)))
            .Select(member => member.Groups["name"].Value)
            .ToList();

    private static bool IsMutable(Match member) =>
        member.Groups["end"].Value.StartsWith('{')
            ? SetterPattern().IsMatch(member.Groups["end"].Value)
            : !member.Groups["readonly"].Success
              || GrowableTypePattern().IsMatch(member.Groups["type"].Value)
              || ArrayTypePattern().IsMatch(member.Groups["type"].Value);

    /// <summary>A read-only type does not stop a caller from casting a growable collection back and changing it.</summary>
    private static bool HoldsNewGrowableCollection(string initializer) =>
        NewGrowableCollectionPattern().IsMatch(initializer) && !FrozenCopyPattern().IsMatch(initializer);

    /// <returns>The text from the member's '=' up to the next ';'; empty for a member without an initializer.</returns>
    private static string Initializer(string code, Match member)
    {
        var end = member.Groups["end"];
        var start = end.Value.StartsWith('{') ? AfterPropertyInitializerSign(code, end.Index + end.Length) : end.Value == "=" ? end.Index + end.Length : -1;
        if (start < 0) return string.Empty;
        var stop = code.IndexOf(';', start);
        return stop < 0 ? code[start..] : code[start..stop];
    }

    /// <param name="accessorsEnd">Where the accessor text the member pattern took ends, just before the closing brace.</param>
    /// <returns>The position after the property's '=', or -1 when it has no initializer.</returns>
    private static int AfterPropertyInitializerSign(string code, int accessorsEnd)
    {
        var match = PropertyInitializerSignPattern().Match(code, accessorsEnd);
        return match.Success && match.Index == accessorsEnd ? match.Index + match.Length : -1;
    }

    [GeneratedRegex(@"\}\s*=(?![>=])")]
    private static partial Regex PropertyInitializerSignPattern();

    [GeneratedRegex(@"\bnew\s+(?:[\w.]+\.)?(?:Concurrent\w+|Dictionary|HashSet|List|Queue|Stack|Sorted\w+)\s*<")]
    private static partial Regex NewGrowableCollectionPattern();

    [GeneratedRegex(@"\.To(?:Frozen|Immutable)\w*\(")]
    private static partial Regex FrozenCopyPattern();

    [GeneratedRegex(@"^\s*(?:\[[^\]]*\]\s*)?(?:(?:public|private|internal|protected|new|volatile)\s+)*static\s+(?<readonly>readonly\s+)?(?<type>(?:[\w.]+(?:<[^=;{()]*>)?|\([^()=;]*\))(?:\[\])*\??)\s+(?<name>\w+)\s*(?<end>;|=(?![>=])|\{[^}\n]*)", RegexOptions.Multiline)]
    private static partial Regex StaticMemberPattern();

    [GeneratedRegex(@"\[\]\??$")]
    private static partial Regex ArrayTypePattern();

    [GeneratedRegex(@"\bset\b")]
    private static partial Regex SetterPattern();

    [GeneratedRegex(@"^(?:\w+\.)*(Concurrent\w+|I?Dictionary|HashSet|ISet|I?List|Queue|Stack|Sorted\w+|ConditionalWeakTable|MemoryCache|ThreadLocal|Lazy|StringBuilder|ComputedOnce\w*|IndexMemo\w*|EvictingLeasedStore\w*)\b")]
    private static partial Regex GrowableTypePattern();
}
