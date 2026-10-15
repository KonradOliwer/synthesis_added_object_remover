namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <summary>The parts of the also-remove step that record their unexpected errors; none has a subject.</summary>
public static class RestingObjectsParts
{
    private const string Owner = "RemoveObjectsRestingOnRemovedOnes";

    /// <summary>The also-remove step as a whole.</summary>
    public static readonly PartKey AlsoRemove = new(Owner, nameof(AlsoRemove));

    /// <summary>The search for the chains of touching objects.</summary>
    public static readonly PartKey TouchChains = new(Owner, nameof(TouchChains));

    /// <summary>Computing the evidence only the report files show.</summary>
    public static readonly PartKey ReportFileDetails = new(Owner, nameof(ReportFileDetails));
}
