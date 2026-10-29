using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>What the decisions do with a target object that a round removes.</summary>
public abstract record WhenRemoved
{
    /// <summary>The object is disabled in the patch, so it stands nowhere.</summary>
    public sealed record Disable : WhenRemoved;

    /// <summary>The object goes back to the earlier version of its record.</summary>
    /// <param name="To">The version it goes back to.</param>
    public sealed record Restore(EarlierVersion To) : WhenRemoved;
}
