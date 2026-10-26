namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

/// <summary>The part of the visible left-behind step that records its unexpected errors; it has no subject.</summary>
public static class VisibleLeftBehindParts
{
    private const string Owner = "RemoveVisibleObjectsLeftBehind";

    /// <summary>The visible left-behind step as a whole.</summary>
    public static readonly PartKey VisibleLeftBehind = new(Owner, nameof(VisibleLeftBehind));
}
