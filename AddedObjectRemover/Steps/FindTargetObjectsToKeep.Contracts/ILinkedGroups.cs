namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

/// <summary>Target objects connected by links between them; a group is removed whole or kept whole.</summary>
public interface ILinkedGroups
{
    /// <summary>The target's group, the target included.</summary>
    IReadOnlyList<int> MembersOf(int targetIndex);

    bool IsLinked(int targetIndex);

    /// <summary>The groups with more than one member.</summary>
    IEnumerable<IReadOnlyList<int>> MultiMemberGroups { get; }
}
