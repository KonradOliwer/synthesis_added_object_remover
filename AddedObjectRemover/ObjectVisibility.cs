namespace AddedObjectRemover;

/// <summary>Whether a placed object can be seen in game, and if not, what kind of invisible object it is.</summary>
/// <param name="Kind">Null for a visible object, one whose base was not found, or one with an effect-only mesh.</param>
/// <param name="BaseMissing">The base object is not set or not in the load order, so the game shows nothing.</param>
/// <param name="EffectOnlyMesh">The mesh holds only effect-shader shapes (fog, light rays, water spray), nothing solid.</param>
internal readonly record struct ObjectVisibility(InvisibleObjectKind? Kind, bool BaseMissing, bool EffectOnlyMesh = false)
{
    public static ObjectVisibility Visible => new(null, false);

    public static ObjectVisibility MissingBase => new(null, true);

    public static ObjectVisibility EffectOnly => new(null, false, EffectOnlyMesh: true);

    public static ObjectVisibility Invisible(InvisibleObjectKind kind) => new(kind, false);

    public bool IsVisible => Kind == null && !BaseMissing && !EffectOnlyMesh;

    /// <summary>Why the object is invisible, e.g. "Lights" or "base not found"; "visible" otherwise.</summary>
    public string Describe()
    {
        if (BaseMissing) return "base not found";
        if (EffectOnlyMesh) return "effect-only mesh";
        return Kind?.ToString() ?? "visible";
    }

    /// <returns>Indices of the visible entries of <paramref name="visibility"/> that are not in <paramref name="except"/>.</returns>
    public static IEnumerable<int> VisibleIndices(IReadOnlyList<ObjectVisibility> visibility, IReadOnlySet<int> except) =>
        Enumerable.Range(0, visibility.Count).Where(index => visibility[index].IsVisible && !except.Contains(index));
}
