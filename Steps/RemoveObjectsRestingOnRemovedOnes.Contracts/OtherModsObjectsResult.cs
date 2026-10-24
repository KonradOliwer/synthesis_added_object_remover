using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <summary>What became of another mod's object left without support.</summary>
public enum OtherModsObjectFate
{
    /// <summary>Moved straight down onto what is below it.</summary>
    PulledDown,

    /// <summary>Removed for being left without support.</summary>
    Removed,

    /// <summary>Removed because nothing was below it within <see cref="OtherModsObjectsLimits.MaxPullDistance"/>.</summary>
    RemovedNothingBelow,

    /// <summary>Left where it stands: it still touches what is below it.</summary>
    LeftStillTouching,

    /// <summary>Left where it stands: something depends on it or controls it (<see cref="OtherModsObjectOutcome.Blocking"/>).</summary>
    LeftNotRemovable,

    /// <summary>
    /// Left where it stands: nothing was below it within <see cref="OtherModsObjectsLimits.MaxPullDistance"/>, and something depends on it or
    /// controls it (<see cref="OtherModsObjectOutcome.Blocking"/>).
    /// </summary>
    LeftNothingBelowNotRemovable,

    /// <summary>Left as it is: a plugin that loads after the patch wins it, so the patch cannot change it.</summary>
    LeftWonAfterPatch,
}

/// <param name="LostShare">The share (0-1) of its support it no longer has where it stood.</param>
/// <param name="Drop">How far it was moved down, in game units; 0 unless it was pulled down.</param>
/// <param name="Blocking">What kept it from being removed; none unless that is its fate.</param>
public sealed record OtherModsObjectOutcome(OtherObject Object, OtherModsObjectFate Fate, float LostShare, float Drop, ReferenceRoles Blocking)
{
    /// <summary>Where it stands now: its position, moved down by <see cref="Drop"/>.</summary>
    public Vector3 NewPosition => Object.Position with { Z = Object.Position.Z - Drop };

    /// <summary>Whether it no longer stands where it stood: pulled down or removed.</summary>
    public bool IsTakenAway => Fate is OtherModsObjectFate.PulledDown or OtherModsObjectFate.Removed or OtherModsObjectFate.RemovedNothingBelow;
}

/// <summary>What the step did with the other mods' objects left without support.</summary>
/// <param name="Objects">Every such object once, in <see cref="OtherId"/> order.</param>
/// <param name="Rounds">
/// The also-remove rounds that removed the target objects resting on or touching the objects taken away (removed or pulled
/// down), and what went with those, in order.
/// </param>
/// <param name="ProposedRounds">Each of those rounds' proposals, in round order, for the run's decisions to apply as rounds.</param>
/// <param name="Failure">Set when an unexpected error stopped the step; nothing was then removed or moved.</param>
public sealed record OtherModsObjectsResult(
    StepOutcome Outcome,
    ImmutableArray<OtherModsObjectOutcome> Objects,
    ImmutableArray<Round> Rounds,
    ImmutableArray<IReadOnlyList<ProposedRemoval>> ProposedRounds,
    PartFailure? Failure)
{
    public static OtherModsObjectsResult SwitchedOff { get; } = new(StepOutcome.SwitchedOff, [], [], [], Failure: null);

    /// <summary>Each object taken away, where it stands now (pulled down), or null (removed), by id.</summary>
    public IReadOnlyDictionary<OtherId, OtherObject?> TakenAway() =>
        Objects.Where(outcome => outcome.IsTakenAway)
            .ToDictionary(outcome => outcome.Object.Id, outcome => outcome.Fate == OtherModsObjectFate.PulledDown ? outcome.Object with { Position = outcome.NewPosition } : (OtherObject?)null);
}
