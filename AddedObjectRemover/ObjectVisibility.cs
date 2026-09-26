namespace AddedObjectRemover;

/// <summary>Whether a placed object can be seen in game, and if not, what kind of invisible object it is.</summary>
/// <param name="Kind">Null for a visible object or one whose base was not found.</param>
/// <param name="BaseMissing">The base object is not set or not in the load order, so the game shows nothing.</param>
internal readonly record struct ObjectVisibility(InvisibleObjectKind? Kind, bool BaseMissing)
{
    public static ObjectVisibility Visible => new(null, false);

    public static ObjectVisibility MissingBase => new(null, true);

    public static ObjectVisibility Invisible(InvisibleObjectKind kind) => new(kind, false);

    public bool IsVisible => Kind == null && !BaseMissing;

    /// <summary>Why the object is invisible, e.g. "Lights" or "base not found"; "visible" otherwise.</summary>
    public string Describe() => BaseMissing ? "base not found" : Kind?.ToString() ?? "visible";
}
