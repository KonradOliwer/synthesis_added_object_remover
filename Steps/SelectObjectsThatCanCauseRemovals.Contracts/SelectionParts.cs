namespace AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

/// <summary>The part of the select step that records its unexpected errors.</summary>
public static class SelectionParts
{
    private const string Owner = "SelectObjectsThatCanCauseRemovals";

    /// <summary>Measuring one other-mod object for whether it can cause removals; the subject is the object.</summary>
    public static readonly PartKey RemovalCauseSelection = new(Owner, nameof(RemovalCauseSelection));
}
