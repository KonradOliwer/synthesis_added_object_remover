namespace AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;

/// <summary>Another mod's object that a target object replaces.</summary>
/// <param name="By">The first matching target object.</param>
public sealed record Replacement(OtherId OtherModObject, TargetId By, float Distance, float SizeRatio);

/// <summary>The replaced other-mod objects: objects that stay indexed but never match in the too-close test.</summary>
public sealed class Replacements
{
    private readonly IdSet _replaced;

    private Replacements(int otherModObjectCount, IReadOnlyList<Replacement> list, IEnumerable<OtherId> excluded) =>
        (_replaced, List) = (IdSet.Of(otherModObjectCount, list.Select(replacement => replacement.OtherModObject).Concat(excluded).Select(id => id.Index)), list);

    /// <summary>In <see cref="TargetId"/> order, then in the order the matches were found.</summary>
    public IReadOnlyList<Replacement> List { get; }

    public int Count => List.Count;

    public static Replacements Of(int otherModObjectCount, IReadOnlyList<Replacement> list) => new(otherModObjectCount, list, []);

    /// <summary>No replacement is listed, but the given objects never match either: they are no cause of removals.</summary>
    /// <param name="excluded">Each must be one of the other-mod objects.</param>
    public static Replacements Excluding(int otherModObjectCount, IEnumerable<OtherId> excluded) => new(otherModObjectCount, [], excluded);

    /// <summary>False for ids beyond the other-mod objects, such as support-only objects, which are never replaced.</summary>
    public bool IsReplaced(OtherId id) => _replaced.Contains(id.Index);
}
