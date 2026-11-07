using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>
/// What the removal steps' follow-up does once the placed records and the ground go back, as the patch writes it after the run's own orders: the
/// cleaned mod's objects removed or restored as *Also remove what rests on removed objects* decides, and the objects of plugins trusted
/// in full that lose their support handled as *Other mods' objects left without support* decides.
/// </summary>
/// <param name="Targets">In target order.</param>
/// <param name="Others">
/// In id order; never one the pull finds still resting where it stands. One left where it stands for another reason is only reported,
/// the patch writes nothing for it.
/// </param>
public sealed record RestoreFollowUp(ImmutableArray<FollowUpTarget> Targets, ImmutableArray<FollowUpOtherObject> Others)
{
    public static RestoreFollowUp None { get; } = new([], []);
}

/// <param name="RestoredTo">The version before the cleaned mod it goes back to, by its when-removed rule; null when it is removed.</param>
/// <param name="StartedBy">The placed records going back it went with, in key order; none when only the ground started it.</param>
public sealed record FollowUpTarget(TargetId Target, EarlierVersion? RestoredTo, ImmutableArray<RecordKey> StartedBy)
{
    /// <summary>Whether ground going back down started it too, directly or through what went with it.</summary>
    public bool ByGround { get; init; }

    /// <summary>The LAND records whose ground going back down started it, under it or under what went with it, in key order.</summary>
    public ImmutableArray<RecordKey> GroundLands { get; init; } = [];
}

/// <param name="Outcome">Its object where it stands before the follow-up: where the run left it.</param>
/// <param name="StartedBy">
/// The placed records going back it lost its support from, directly or through what went with them, in key order; none when only the
/// ground started it.
/// </param>
public sealed record FollowUpOtherObject(OtherModsObjectOutcome Outcome, ImmutableArray<RecordKey> StartedBy)
{
    /// <summary>Whether the ground under it, or under what went with it, going back down started it too.</summary>
    public bool ByGround { get; init; }

    /// <summary>The LAND records whose ground going back down started it, under it or under what went with it, in key order.</summary>
    public ImmutableArray<RecordKey> GroundLands { get; init; } = [];
}
