namespace AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;

/// <summary>The part of the judge-changed-objects step that records its unexpected errors.</summary>
public static class JudgeChangedObjectsParts
{
    private const string Owner = "JudgeChangedObjects";

    /// <summary>Checking an earlier version at its old place; the subject is the changed object.</summary>
    public static readonly PartKey OldPlace = new(Owner, nameof(OldPlace));
}
