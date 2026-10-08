namespace AddedObjectRemover;

/// <summary>A set of dense ids below a known count, answered by array lookup.</summary>
public sealed class IdSet
{
    private readonly bool[] _members;

    private IdSet(bool[] members) => _members = members;

    /// <param name="count">Ids are dense: every id is at least 0 and below this count.</param>
    public static IdSet Of(int count, IEnumerable<int> ids)
    {
        var members = new bool[count];
        foreach (var id in ids) members[id] = true;
        return new IdSet(members);
    }

    /// <summary>False for every id outside the count, instead of an error.</summary>
    public bool Contains(int id) => (uint)id < (uint)_members.Length && _members[id];
}
