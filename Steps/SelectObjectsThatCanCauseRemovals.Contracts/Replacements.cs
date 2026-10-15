namespace AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

/// <summary>Another mod's object that a target object replaces.</summary>
/// <param name="By">The first matching target object.</param>
public sealed record Replacement(OtherId OtherModObject, TargetId By, float Distance, float SizeRatio);

/// <summary>The replaced other-mod objects: objects that stay indexed but never match in the too-close test.</summary>
public sealed class Replacements
{
    private readonly IdSet _replaced;

    private Replacements(int otherModObjectCount, IReadOnlyList<Replacement> list)
    {
        _replaced = IdSet.Of(otherModObjectCount, list.Select(replacement => replacement.OtherModObject.Index));
        List = list;
    }

    /// <summary>In <see cref="TargetId"/> order, then in the order the matches were found.</summary>
    public IReadOnlyList<Replacement> List { get; }

    public int Count => List.Count;

    public static Replacements None(int otherModObjectCount) => new(otherModObjectCount, []);

    public static Replacements Of(int otherModObjectCount, IReadOnlyList<Replacement> list) => new(otherModObjectCount, list);

    /// <summary>False for ids beyond the other-mod objects, such as support-only objects, which are never replaced.</summary>
    public bool IsReplaced(OtherId id) => _replaced.Contains(id.Index);
}
