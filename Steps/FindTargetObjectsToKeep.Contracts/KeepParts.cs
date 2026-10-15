namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

/// <summary>The part of the keep step that records its unexpected errors.</summary>
public static class KeepParts
{
    private const string Owner = "FindTargetObjectsToKeep";

    /// <summary>Finding whether one target object is to be kept; the subject is the object.</summary>
    public static readonly PartKey KeepCheck = new(Owner, nameof(KeepCheck));
}
