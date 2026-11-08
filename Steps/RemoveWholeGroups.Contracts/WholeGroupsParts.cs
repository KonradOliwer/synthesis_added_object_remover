namespace AddedObjectRemover.Steps.RemoveWholeGroups.Contracts;

/// <summary>The part of the step for whole groups that records its unexpected errors; it has no subject.</summary>
public static class WholeGroupsParts
{
    private const string Owner = "RemoveWholeGroups";

    /// <summary>The step as a whole.</summary>
    public static readonly PartKey WholeGroups = new(Owner, nameof(WholeGroups));
}
