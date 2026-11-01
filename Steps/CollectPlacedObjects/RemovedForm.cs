using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>
/// Whether a placed record is removed in the game, read from the flags of its final version: it is deleted, or Initially
/// Disabled without an enable parent, or has the player as enable parent with the opposite enable state, whatever Initially
/// Disabled says (<see cref="RemovalConvention"/>). A record that is not removed this way follows its enable parent, which only a
/// reader of the parent's own presence can tell (<see cref="PlacedPresence"/>), unless it is a winning target that is Initially
/// Disabled, which <see cref="StartsDisabledRule"/> hides.
/// </summary>
/// <remarks>
/// The depth a removal moves an object to is never a criterion: depth alone would be a guess, while the flags are the game's
/// convention.
/// </remarks>
internal static class RemovedForm
{
    public static bool IsRemoved(PresenceFacts presence) =>
        presence.Deleted
        || presence is { InitiallyDisabled: true, EnableParent: null }
        || presence.EnableParent == new EnableParentFact(RemovalConvention.PlayerRef, RemovalConvention.OppositeOfParent);

    public static PlacedPresence PresenceOf(PresenceFacts presence, bool isWinningTarget) =>
        new(IsRemoved(presence) || StartsDisabledRule.HidesWinningTarget(presence, isWinningTarget), presence.EnableParent?.Parent, presence.EnableParent?.Opposite ?? false);
}
