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
    /// A removed record (<see cref="RemovedForm"/>) never reaches this rule, so an initially disabled record here has an Enable
    /// Parent. See <see cref="HidesWinningTarget"/> for what that flag does. A record without a placement is never shown. A
    /// non-finite rotation would make every oriented box test report a hit, so it makes the placement invalid, as does a position
    /// out of range.
    /// </summary>
    public static ShownInGame Of(PlacementFacts facts, bool isWinningTarget)
    {
        if (HidesWinningTarget(facts.Presence, isWinningTarget)) return ShownInGame.Hidden;
        if (facts is not { Position: { } position, EulerRotation: { } rotation }) return ShownInGame.Hidden;
        return Vectors.IsWithinLimit(position, PlacementLimit) && Vectors.IsFinite(rotation) ? ShownInGame.Present : ShownInGame.InvalidPlacement;
    }

    /// <summary>
    /// Initially Disabled hides a winning target record whatever its enable parent says; on any other record the parent's state
    /// decides whether the game shows it, so it counts as present.
    /// </summary>
    public static bool HidesWinningTarget(PresenceFacts presence, bool isWinningTarget) => presence.InitiallyDisabled && isWinningTarget;
}
