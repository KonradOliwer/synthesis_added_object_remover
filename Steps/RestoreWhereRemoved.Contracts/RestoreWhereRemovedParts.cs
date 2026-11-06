namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>The parts of the step that restores what the cleaned mod changed where it was removed, which record their unexpected errors.</summary>
public static class RestoreWhereRemovedParts
{
    private const string Owner = "RestoreWhereRemoved";

    /// <summary>The whole step; it has no subject.</summary>
    public static readonly PartKey Step = new(Owner, nameof(Step));

    /// <summary>Checking one placed record of another plugin against what stays; the subject is the record.</summary>
    public static readonly PartKey ObjectCheck = new(Owner, nameof(ObjectCheck));

    /// <summary>Measuring one object that stays, which the records going back are checked against; the subject is the object.</summary>
    public static readonly PartKey ObjectThatStays = new(Owner, nameof(ObjectThatStays));

    /// <summary>
    /// Checking one object that stays, or a copy the patch adds, for changed terrain in the world as the restore plans it; the subject
    /// is the object, or the copy's original.
    /// </summary>
    public static readonly PartKey StandingTerrainCheck = new(Owner, nameof(StandingTerrainCheck));
}
