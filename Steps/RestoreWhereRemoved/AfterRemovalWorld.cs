using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// The world once every round and every move is done. Its causes are the targets that stand, where they stand last (fitted, moved or
/// restored), then the objects of other plugins that are a change in the world without the cleaned mod and those the patch pulled
/// down, where they stand. Its support-only objects are every other object that stands, such as one that stood there without the
/// cleaned mod. The other mods' objects the patch removed are in neither. Every candidate that stands now is among them, where it
/// stands now.
/// </summary>
/// <param name="World">
/// A scan result the object caches index: the causes and support-only objects within the guards' reach (<see cref="GuardReach"/>),
/// as other-mod and support-only objects, and the candidates, each as it goes back, with the candidate's index as its id.
/// </param>
/// <param name="Causes">Every cause, in the order above.</param>
/// <param name="SupportOnly">Every support-only object, in id order of the scan.</param>
/// <param name="CleanedModObjectsThatStand">The cleaned mod's own objects that stand, where they stand last, in target order.</param>
/// <param name="StandingNow">The candidate index of each candidate that stands now, ascending.</param>
/// <param name="Now">
/// The candidates that stand now, where they stand once every round and every move is done, in the order of <paramref name="StandingNow"/>;
/// the id of each is its position.
/// </param>
/// <param name="CopiesNow">The id among the other objects of <paramref name="World"/> of each of <paramref name="Now"/>.</param>
/// <param name="CausesRestoredByRemovalSteps">
/// The ids among the other-mod objects of <paramref name="World"/> of the targets a removal step restored to their earlier version,
/// in id order.
/// </param>
/// <param name="EndInEarlierShapeAndPlace">
/// By candidate index: the candidate as it goes back has the same shape where it stands as with every part taken from its earlier
/// version (<see cref="PlacedShape.IsSameAs"/>). One that keeps a placement, model or collision data a later plugin changed does not;
/// a kept part that shapes nothing, such as the enable state, does not count.
/// </param>
/// <param name="KeepShapeWhereItStandsNow">
/// By candidate index: the candidate stands now and as it goes back has the same shape where it stands as now
/// (<see cref="PlacedShape.IsSameAs"/>), such as one that keeps the position a later plugin gave it and whose undone parts shape
/// nothing.
/// </param>
internal sealed record AfterRemovalWorld(
    CollectedObjects World,
    ImmutableArray<OtherObject> Causes,
    ImmutableArray<OtherObject> SupportOnly,
    ImmutableArray<TargetObject> CleanedModObjectsThatStand,
    ImmutableArray<int> StandingNow,
    ImmutableArray<TargetObject> Now,
    ImmutableArray<OtherId> CopiesNow,
    ImmutableArray<OtherId> CausesRestoredByRemovalSteps,
    ImmutableArray<bool> EndInEarlierShapeAndPlace,
    ImmutableArray<bool> KeepShapeWhereItStandsNow)
{
    /// <summary>Every part of a placed record, so a restore that undoes them all gives the earlier version.</summary>
    private static readonly PlacedParts EveryPart = Enum.GetValues<PlacedParts>().Aggregate(PlacedParts.None, (parts, part) => parts | part);

    /// <summary>The candidates, each as it goes back, by candidate index.</summary>
    public ImmutableArray<TargetObject> Restored => World.Targets;

    /// <param name="candidates">The placed records whose areas let them go back, in key order.</param>
    /// <param name="reach">How far the guards that read the object caches look.</param>
    public static AfterRemovalWorld Of(
        CollectedObjects collected,
        IRemovalDecisions final,
        OtherModsObjectsResult otherModsObjects,
        PluginName cleanedMod,
        IReadOnlyList<RecordChangedByCleanedMod> candidates,
        GuardReach reach)
    {
        var standingTargets = TargetsWhereTheyStandLast(collected.Targets, final);
        var takenAway = otherModsObjects.TakenAway();
        var others = OtherObjectsThatStand(collected, takenAway);
        ImmutableArray<OtherObject> causes = [.. standingTargets.Select(target => target.AsOtherObject(default, cleanedMod)).Concat(others.Where(other => IsChange(other, takenAway)))];
        ImmutableArray<OtherObject> supportOnly = [.. others.Where(other => !IsChange(other, takenAway))];
        var standingKeys = causes.Concat(supportOnly).Select(other => other.Key).ToHashSet();
        var asItIsNow = candidates.Select((change, index) => AsItIsNow(change, new TargetId(index), collected.Targets)).ToList();
        ImmutableArray<TargetObject> restored = [.. candidates.Select((change, index) => asItIsNow[index].RestoredAs(change.Earlier))];
        ImmutableArray<int> standingNow = [.. Enumerable.Range(0, candidates.Count).Where(index => standingKeys.Contains(candidates[index].Key))];
        var standsLast = standingTargets.ToDictionary(target => target.Id);
        ImmutableArray<TargetObject> now = [.. standingNow.Select((candidate, position) => WhereItStandsNow(candidates[candidate], asItIsNow[candidate], standsLast) with { Id = new TargetId(position) })];

        var inReach = InReach(causes, supportOnly, restored, now, reach);
        var nearCauses = causes.Where(other => inReach.Contains(other.Key)).ToList();
        var objects = nearCauses.Concat(supportOnly.Where(other => inReach.Contains(other.Key))).Select((other, index) => other with { Id = new OtherId(index) }).ToList();
        var idOf = objects.ToDictionary(other => other.Key, other => other.Id);
        var restoredByRemovalSteps = standingTargets.Where(target => final.Of(target.Id) is Decision.Restored).Select(target => target.Key).ToHashSet();
        var world = collected with
        {
            Targets = restored,
            OtherModObjects = [.. objects.Take(nearCauses.Count)],
            SupportOnlyObjects = [.. objects.Skip(nearCauses.Count)],
            Order = new WorkOrder([.. Enumerable.Range(0, candidates.Count)]),
        };
        return new AfterRemovalWorld(
            world,
            causes,
            supportOnly,
            [.. standingTargets.Where(target => target.Origin == TargetOrigin.CleanedMod)],
            standingNow,
            now,
            [.. standingNow.Select(candidate => idOf[candidates[candidate].Key])],
            [.. nearCauses.Where(cause => restoredByRemovalSteps.Contains(cause.Key)).Select(cause => idOf[cause.Key])],
            [.. candidates.Select((change, index) => PlacedShape.Of(restored[index]).IsSameAs(PlacedShape.Of(asItIsNow[index].RestoredAs(change.Earlier with { Undo = EveryPart }))))],
            KeepShapeWhereTheyStandNow(restored, standingNow, now));
    }

    /// <param name="standingNow">The candidate index of each candidate that stands now.</param>
    /// <param name="now">Those candidates where they stand now, in the same order.</param>
    private static ImmutableArray<bool> KeepShapeWhereTheyStandNow(ImmutableArray<TargetObject> restored, ImmutableArray<int> standingNow, ImmutableArray<TargetObject> now)
    {
        var shapeNow = standingNow.Zip(now).ToDictionary(entry => entry.First, entry => PlacedShape.Of(entry.Second));
        return [.. restored.Select((candidate, index) => shapeNow.TryGetValue(index, out var shape) && PlacedShape.Of(candidate).IsSameAs(shape))];
    }

    /// <summary>
    /// The ids among the other objects of <see cref="World"/> of the objects that stand as they stood before the cleaned mod: the
    /// support-only objects whose winning version loads before it, and the causes a removal step restored to their earlier version.
    /// A support-only object of a later plugin may be one that plugin added or moved for the world the cleaned mod made, so it is not
    /// among them.
    /// </summary>
    public HashSet<OtherId> StandingAsBeforeTheCleanedMod(Func<PluginName, bool> loadsBeforeCleanedMod) =>
        [.. World.SupportOnlyObjects.Where(other => loadsBeforeCleanedMod(other.WinningMod)).Select(other => other.Id), .. CausesRestoredByRemovalSteps];

    /// <summary>The keys of the objects within the guards' reach, and of every candidate's copy where it stands now.</summary>
    private static HashSet<RecordKey> InReach(
        ImmutableArray<OtherObject> causes, ImmutableArray<OtherObject> supportOnly, ImmutableArray<TargetObject> restored, ImmutableArray<TargetObject> now, GuardReach reach)
    {
        List<OtherObject> objects = [.. causes, .. supportOnly];
        var inReach = reach.InReach(objects, restored, now);
        return [.. objects.Zip(inReach).Where(entry => entry.Second).Select(entry => entry.First.Key).Concat(now.Select(candidate => candidate.Key))];
    }

    /// <summary>A candidate that is a target where it stands last, with every fit, restore and move; any other as it is now.</summary>
    /// <param name="standsLast">The targets that are not removed, where they stand last, by id.</param>
    private static TargetObject WhereItStandsNow(RecordChangedByCleanedMod change, TargetObject asItIsNow, IReadOnlyDictionary<TargetId, TargetObject> standsLast) =>
        change.Target is { } target ? standsLast[target] : asItIsNow;

    /// <summary>The ids among the other objects of <see cref="World"/> of the given candidates that stand now, in candidate order.</summary>
    public IEnumerable<OtherId> CopiesNowOf(IReadOnlySet<int> candidates) =>
        Now.Where(standing => candidates.Contains(StandingNow[standing.Id.Index])).Select(standing => CopiesNow[standing.Id.Index]);

    /// <summary>
    /// The ids among the other-mod objects of <see cref="World"/> of the given candidates that stand now, in candidate order. A copy
    /// among the support-only objects, one that is no change in the world without the cleaned mod, causes no removals anyway.
    /// </summary>
    public IEnumerable<OtherId> CauseCopiesNowOf(IReadOnlySet<int> candidates) => CopiesNowOf(candidates).Where(id => id.Index < World.OtherModObjects.Length);

    /// <summary>
    /// The candidates outside the given ones that stand now, where they stand, in candidate order. A candidate the cleaned mod
    /// disabled or deleted stands nowhere, so it is not among them.
    /// </summary>
    public IEnumerable<TargetObject> NowOfAllBut(IReadOnlySet<int> candidates) => Now.Where(standing => !candidates.Contains(StandingNow[standing.Id.Index]));

    /// <summary>Each target that is not removed, as it stands with every fit and restore, then at the place a move put it.</summary>
    public static List<TargetObject> TargetsWhereTheyStandLast(ImmutableArray<TargetObject> targets, IRemovalDecisions final)
    {
        var movedTo = final.Moves.ToDictionary(move => move.Target, move => move.To);
        return
        [
            .. final.TargetsAsTheyStand(targets)
                .Where(target => !final.IsRemoved(target.Id))
                .Select(target => movedTo.TryGetValue(target.Id, out var to) ? target.PlacedAt(target.Placement with { Position = to }) : target),
        ];
    }

    /// <summary>The objects of other plugins that stand, where they stand, in id order.</summary>
    /// <param name="takenAway">The other mods' objects the patch pulled down (where they stand now) or removed (null).</param>
    private static List<OtherObject> OtherObjectsThatStand(CollectedObjects collected, IReadOnlyDictionary<OtherId, OtherObject?> takenAway) =>
    [
        .. collected.OtherModAndSupportOnlyObjects
            .Select(other => takenAway.TryGetValue(other.Id, out var after) ? after : other)
            .OfType<OtherObject>(),
    ];

    /// <summary>Whether the object is a change in the world without the cleaned mod, or the patch pulled it down.</summary>
    private static bool IsChange(OtherObject other, IReadOnlyDictionary<OtherId, OtherObject?> takenAway) =>
        other.ChangeWithoutCleanedMod is not null || takenAway.ContainsKey(other.Id);

    /// <summary>
    /// The candidate where it stands now, with the id given: a target as the run collected it, any other record from what its winning
    /// version says, at the earlier version's placement when it has none.
    /// </summary>
    /// <remarks>The guards read no origin and no area of a record that is no target, so it carries plain ones.</remarks>
    public static TargetObject AsItIsNow(RecordChangedByCleanedMod change, TargetId id, ImmutableArray<TargetObject> targets)
    {
        if (change.Target is { } target) return targets[target.Index] with { Id = id };

        var placement = change.WhereItIsNow ?? change.Earlier.Placement;
        var reference = change.ReferenceNow;
        return new TargetObject(
            id,
            change.Key,
            change.EditorId,
            change.SpaceKey,
            change.Cell,
            placement.Transform,
            placement.Rotation,
            change.BaseNow,
            reference.IsTeleportDoor,
            reference.IsPrimitive,
            reference.HasMapMarker,
            change.IsPersistent,
            HasEnableParent: false,
            NamedByScript: null,
            HasLocationRefType: false,
            reference.ReferenceRadius,
            reference.PrimitiveBounds,
            reference.CollisionLayer,
            reference.IsCullingVolume,
            new TargetArea(Hold: null, Place: null, PlaceSource.GridOnly, Grid: null),
            TargetOrigin.Moved,
            change.Earlier);
    }
}
