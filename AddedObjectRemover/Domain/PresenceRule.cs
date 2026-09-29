using System.Numerics;

namespace AddedObjectRemover;

/// <param name="Position">Null when the record has no placement.</param>
/// <param name="EulerRotation">Null when the record has no placement.</param>
internal readonly record struct PlacementFacts(bool InitiallyDisabled, bool HasEnableParent, Vector3? Position, Vector3? EulerRotation);

internal enum Presence { Present, Hidden, InvalidPlacement }

/// <summary>Whether the game shows a placed record, and whether its placement is usable.</summary>
internal static class PresenceRule
{
    /// <summary>
    /// Initially Disabled hides a record, except a record that is not a winning target record and has
    /// an Enable Parent: the parent's state decides whether the game shows it, so it counts as present.
    /// A record without a placement is never shown. A non-finite rotation would make every oriented
    /// box test report a hit, so it makes the placement invalid, as does a position out of range.
    /// </summary>
    public static Presence Of(PlacementFacts facts, bool isWinningTarget)
    {
        if (facts.InitiallyDisabled && (isWinningTarget || !facts.HasEnableParent)) return Presence.Hidden;
        if (facts is not { Position: { } position, EulerRotation: { } rotation }) return Presence.Hidden;
        return Geometry.IsWithinLimits(position) && Geometry.IsFinite(rotation) ? Presence.Present : Presence.InvalidPlacement;
    }
}
