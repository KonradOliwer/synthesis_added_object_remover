namespace AddedObjectRemover.Steps.CollectPlacedObjects;

internal enum ShownInGame { Present, Hidden, InvalidPlacement }

/// <summary>Whether the game shows a placed record, and whether its placement is usable.</summary>
internal static class StartsDisabledRule
{
    /// <summary>
    /// Largest accepted absolute placement coordinate. Real content stays far below it (a worldspace
    /// spans a few hundred thousand units); larger values come from broken records or sentinel values.
    /// </summary>
    private const float PlacementLimit = 1e6f;

    /// <summary>
    /// Initially Disabled hides a record, except a record that is not a winning target record and has
    /// an Enable Parent: the parent's state decides whether the game shows it, so it counts as present.
    /// A record without a placement is never shown. A non-finite rotation would make every oriented
    /// box test report a hit, so it makes the placement invalid, as does a position out of range.
    /// </summary>
    public static ShownInGame Of(PlacementFacts facts, bool isWinningTarget)
    {
        if (facts.InitiallyDisabled && (isWinningTarget || !facts.HasEnableParent)) return ShownInGame.Hidden;
        if (facts is not { Position: { } position, EulerRotation: { } rotation }) return ShownInGame.Hidden;
        return Vectors.IsWithinLimit(position, PlacementLimit) && Vectors.IsFinite(rotation) ? ShownInGame.Present : ShownInGame.InvalidPlacement;
    }
}
