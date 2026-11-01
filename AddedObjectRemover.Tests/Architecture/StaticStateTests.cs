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
            """;

        Assert.Equal(["_count", "Name", "Cache", "Seen", "Value", "Fields", "Directions", "Pairs", "Checks"], MutableStatics(code));
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
            """;

        Assert.Empty(MutableStatics(code));
    }

    private static List<string> MutableStatics(string code) =>
        StaticMemberPattern().Matches(code)
            .Where(member => IsMutable(member))
            .Select(member => member.Groups["name"].Value)
            .ToList();

    private static bool IsMutable(Match member) =>
        member.Groups["end"].Value.StartsWith('{')
            ? SetterPattern().IsMatch(member.Groups["end"].Value)
            : !member.Groups["readonly"].Success
              || GrowableTypePattern().IsMatch(member.Groups["type"].Value)
              || ArrayTypePattern().IsMatch(member.Groups["type"].Value);

    [GeneratedRegex(@"^\s*(?:\[[^\]]*\]\s*)?(?:(?:public|private|internal|protected|new|volatile)\s+)*static\s+(?<readonly>readonly\s+)?(?<type>(?:[\w.]+(?:<[^=;{()]*>)?|\([^()=;]*\))(?:\[\])*\??)\s+(?<name>\w+)\s*(?<end>;|=(?![>=])|\{[^}\n]*)", RegexOptions.Multiline)]
    private static partial Regex StaticMemberPattern();

    [GeneratedRegex(@"\[\]\??$")]
    private static partial Regex ArrayTypePattern();

    [GeneratedRegex(@"\bset\b")]
    private static partial Regex SetterPattern();

    [GeneratedRegex(@"^(?:\w+\.)*(Concurrent\w+|I?Dictionary|HashSet|ISet|I?List|Queue|Stack|Sorted\w+|ConditionalWeakTable|MemoryCache|ThreadLocal|Lazy|StringBuilder|ComputedOnce\w*|IndexMemo\w*|EvictingLeasedStore\w*)\b")]
    private static partial Regex GrowableTypePattern();
}
