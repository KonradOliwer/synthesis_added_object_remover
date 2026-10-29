namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>The version of a placed object that the game shows without the cleaned mod.</summary>
/// <param name="Plugin">The plugin the version comes from.</param>
/// <param name="Placement">Where and how the object stands in that version, the scale normalized.</param>
/// <param name="StoredScale">The scale as the version stores it; null when it has none.</param>
/// <param name="Base">The version's base object; null when its base link is empty.</param>
/// <param name="Cell">The cell that plugin puts the record in.</param>
/// <param name="IsPersistent">The version's own Persistent flag is set.</param>
/// <param name="RestoresWholeRecord">
/// The version's base object is not the winning version's, so every field that goes with the base must come back with it:
/// restoring writes the whole record of <paramref name="Plugin"/>, not just the placement.
/// </param>
/// <param name="Reference">What the version says about the reference itself; it stands with these only when it restores the whole record.</param>
public sealed record EarlierVersion(
    PluginName Plugin,
    EulerPlacement Placement,
    float? StoredScale,
    BaseKey? Base,
    CellFact Cell,
    bool IsPersistent,
    bool RestoresWholeRecord,
    ReferenceFacts Reference);
