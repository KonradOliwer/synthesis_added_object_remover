using System.Collections.Immutable;
using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;

/// <summary>The too-close rule over a world of what stays, whose objects to judge are its target objects.</summary>
public interface ITooCloseToWhatStaysRule
{
    /// <summary>
    /// The rule over <paramref name="world"/>, a world of what stays whose objects to judge are its target objects, with its other
    /// objects as causes except <paramref name="leftOut"/>.
    /// </summary>
    IPlacementRule Create(CollectedObjects world, IEnumerable<OtherId> leftOut, TooCloseSettings settings);

    /// <summary>Every cause of <paramref name="world"/> but <paramref name="leftOut"/> that the target object, where it stands, is too close to.</summary>
    TooCloseClashes FindEveryClash(CollectedObjects world, IEnumerable<OtherId> leftOut, TooCloseSettings settings, TargetObject target);

    /// <summary>The box around the object, at any angle, that holds every zone the rule grows around it with the settings.</summary>
    Box ZoneReachOf(TargetObject placed, TooCloseSettings settings);

    /// <summary>The box the rule finds an other object by: its bounds, or wider.</summary>
    Box BoundsOf(OtherObject other);

    /// <summary>
    /// The positions of the objects whose zones, grown with the settings given beside each, reach one of <paramref name="others"/>,
    /// ascending; every one of them when one of <paramref name="others"/> is a placed NPC, whose body the rule measures on its own.
    /// </summary>
    ImmutableArray<int> FindObjectsReaching(IReadOnlyList<(TargetObject Object, TooCloseSettings Settings)> objects, IReadOnlyList<OtherObject> others);

    /// <summary>
    /// The positions of <paramref name="others"/> that the zones of one of the objects, grown with the settings given beside each,
    /// reach, ascending; a placed NPC always, since the rule measures its body on its own.
    /// </summary>
    ImmutableArray<int> FindOthersReached(IReadOnlyList<(TargetObject Object, TooCloseSettings Settings)> objects, IReadOnlyList<OtherObject> others);
}

/// <summary>What one object's too-close check over every cause found.</summary>
/// <param name="Others">The causes the object is too close to, in id order; those found before the check stopped, when it did.</param>
/// <param name="Failure">Why the check stopped unexpectedly; null when it did not.</param>
public sealed record TooCloseClashes(ImmutableArray<OtherId> Others, string? Failure);
