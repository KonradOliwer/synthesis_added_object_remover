using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>What the removal steps' follow-up does with the candidates leaving where they stand now, in one pass.</summary>
/// <param name="HoldingUp">
/// With KeepSupportingObject: each candidate whose leaving leaves an object of a plugin trusted in full without support, with the
/// first such object's key in id order.
/// </param>
/// <param name="CopiesLost">The candidates going back with a copy whose object the follow-up takes along.</param>
/// <param name="Written">
/// What the follow-up decides, each with the candidates it started from: written, or only reported for an object it leaves where it
/// stands although it loses its support.
/// </param>
/// <param name="TargetsDecided">The keys of the target objects of <paramref name="Written"/>, which no longer stand where they stand now.</param>
internal sealed record FollowUpChecks(IReadOnlyDictionary<int, RecordKey> HoldingUp, IReadOnlySet<int> CopiesLost, RestoreFollowUp Written, IReadOnlySet<RecordKey> TargetsDecided)
{
    /// <summary>
    /// The candidates that would not fit where they arrive and that the follow-up may not pull down or remove, each with why: candidates
    /// that are targets, objects of plugins not trusted in full or that the handling or their references keep, and every one that
    /// does not fit for another reason than losing its support.
    /// </summary>
    public IReadOnlyDictionary<int, ArrivalRefused> ArrivingWhereTheyDoNotFit { get; init; } = new Dictionary<int, ArrivalRefused>();

    /// <summary>
    /// The candidates whose going back makes the follow-up restore a target object that would not fit where it goes back, each with the
    /// first such object in target order.
    /// </summary>
    public IReadOnlyDictionary<int, RecordKey> TakingAlongWithoutSupport { get; init; } = new Dictionary<int, RecordKey>();

    /// <summary>The target objects the follow-up restores, each where it goes back to, in target order.</summary>
    public IReadOnlyList<RestoredByFollowUp> TargetsRestored { get; init; } = [];

    /// <summary>
    /// With KeepSupportingObject: the objects of plugins trusted in full the ground going down would leave without support, directly or
    /// through what goes with it, where they stand now, in id order; the ground under them stays.
    /// </summary>
    public IReadOnlyList<OtherObject> GroundHolds { get; init; } = [];

    public static FollowUpChecks None() => new(new Dictionary<int, RecordKey>(), new HashSet<int>(), RestoreFollowUp.None, new HashSet<RecordKey>());

    /// <summary>The keys of the objects of any plugin the follow-up removes.</summary>
    public HashSet<RecordKey> OthersRemoved() => [.. Written.Others.Where(other => other.Outcome.IsRemoved).Select(other => other.Outcome.Object.Key)];

    /// <summary>The objects of any plugin the follow-up pulls down, each where it lands, with the candidates it started from, in id order.</summary>
    public List<PulledByFollowUp> PulledDown() =>
    [
        .. Written.Others.Where(other => other.Outcome.Fate == OtherModsObjectFate.PulledDown)
            .Select(other => new PulledByFollowUp(
                other.Outcome.Object with { Position = other.Outcome.NewPosition }, other.StartedBy, other.ByGround ? other.Outcome.Object : null)),
    ];
}
