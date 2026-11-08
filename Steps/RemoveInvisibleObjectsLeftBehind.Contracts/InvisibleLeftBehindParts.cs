namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

/// <summary>The part of the invisible left-behind step that records its user-data failures; it has no subject.</summary>
public static class InvisibleLeftBehindParts
{
    private const string Owner = "RemoveInvisibleObjectsLeftBehind";

    /// <summary>The invisible left-behind step as a whole.</summary>
    public static readonly PartKey LeftBehind = new(Owner, nameof(LeftBehind));
}
