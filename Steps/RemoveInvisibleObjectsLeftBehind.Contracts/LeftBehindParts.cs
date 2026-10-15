namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

/// <summary>The part of the left-behind step that records its unexpected errors; it has no subject.</summary>
public static class LeftBehindParts
{
    private const string Owner = "RemoveInvisibleObjectsLeftBehind";

    /// <summary>The left-behind step as a whole.</summary>
    public static readonly PartKey LeftBehind = new(Owner, nameof(LeftBehind));
}
