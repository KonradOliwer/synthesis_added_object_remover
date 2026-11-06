using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

/// <summary>
/// The follow-up of objects leaving their place outside the removal rounds: what the removal steps ask after a round, asked about
/// target objects decided on a working copy and about objects of any plugin moved or taken away.
/// </summary>
public interface IFollowUpOfObjectsLeaving
{
    /// <summary>
    /// What goes with what leaves, in this order: the target objects the also-remove mode removes from the removals of the rounds
    /// <paramref name="decided"/> has beyond the decisions of <paramref name="standing"/>; those resting on or touching the objects of
    /// <paramref name="leaving"/> where they stood that lose them, and the rounds that spread from them; then the objects copies stand
    /// for that go with what left by the also-remove mode, as the cleaned mod's own objects do, and the objects of plugins trusted in
    /// full that lose their support between the world of <paramref name="standing"/> and the world after, on the ground of
    /// <see cref="ObjectsLeaving.Land"/>, and the target objects on those taken away, until no further target object leaves. With KeepSupportingObject the objects of plugins trusted in full are
    /// only found, as held up, and nothing is taken away for them. Decided on a working copy of <paramref name="decided"/>, which never
    /// changes; no target object and no copy is judged with the also-remove mode Nothing. The rounds measure the target objects where
    /// <paramref name="decided"/> places them. Last, the objects arriving somewhere that do not fit there (see
    /// <see cref="FollowUpOutcomes.Arriving"/>), in every mode.
    /// </summary>
    /// <param name="decided">A later state of the decisions of <paramref name="standing"/>, whose rounds beyond them decide the target objects that leave.</param>
    FollowUpOutcomes Find(WorldStanding standing, IRemovalDecisions decided, ObjectsLeaving leaving);
}

/// <summary>The world as it stands before anything leaves.</summary>
/// <param name="Decisions">The decisions of the target objects.</param>
/// <param name="OthersTakenAway">The objects of any plugin the run pulled down (where they stand now) or removed (null), by id.</param>
public sealed record WorldStanding(IRemovalDecisions Decisions, IReadOnlyDictionary<OtherId, OtherObject?> OthersTakenAway);

/// <summary>An object of any plugin leaving where it stands now.</summary>
/// <param name="Object">Where it stands now; its id as among the run's objects of any plugin.</param>
/// <param name="To">Where it stands once it left; null when it leaves the world or takes another shape.</param>
public readonly record struct OtherLeaving(OtherObject Object, PlacedTransform? To);

/// <param name="Others">In id order.</param>
/// <param name="Copies">
/// The objects of any plugin that stand for a copy of the cleaned mod's content, which stays where they stand, in id order: judged
/// like the cleaned mod's own objects, by the also-remove mode and its threshold, whatever their plugin, and never as objects of
/// their plugin.
/// </param>
/// <param name="Staying">
/// The objects that stay where they stand now, or that the run took away, although they stood elsewhere before the change that put
/// them there, each with where it would stand once it left: what the objects arriving at their place would rest on had these left
/// too. In id order.
/// </param>
/// <param name="TargetsStaying">
/// The target objects that stand where they stand, or are removed, although they stood elsewhere before the change that put them
/// there, each with the version it would go back to, whatever the decisions say of it.
/// </param>
/// <param name="StayingInAnotherShape">
/// The objects that stand in another shape than before the change that put them there, or that the game no longer shows, disabled or
/// deleted, although they stood before, and stay so; each as it would stand once its changed parts went back: one of
/// <paramref name="Staying"/> in another shape (it leaves with no place) with its own id, one the game no longer shows with an id after
/// the run's objects of any plugin. In id order.
/// </param>
/// <param name="ArrivingInAnotherShape">
/// The objects going back that arrive in another shape than they stand in now, or that the game shows again; each as it stands once it
/// went back: one of <paramref name="Others"/> (it leaves with no place) with its own id, one the game shows again with an id after the
/// run's objects of any plugin, numbered with those of <paramref name="StayingInAnotherShape"/>. In id order. Each stands there in the
/// world after, as support when it can hold up others, and is judged where it arrives when it can rest.
/// </param>
/// <param name="Land">The ground and water once the restore wrote its terrain and cells; null for the ground and water as they are.</param>
public sealed record ObjectsLeaving(
    ImmutableArray<OtherLeaving> Others,
    ImmutableArray<OtherObject> Copies,
    ImmutableArray<OtherLeaving> Staying,
    ImmutableArray<GoingBack> TargetsStaying,
    ImmutableArray<OtherObject> StayingInAnotherShape,
    ImmutableArray<OtherObject> ArrivingInAnotherShape,
    LandAfter? Land);

/// <summary>
/// The ground and water of the world after. Only the objects of plugins trusted in full that rest on ground going down can lose their
/// support to it; the target objects, the objects copies stand for and the world before keep the ground as it is, and ground going up
/// holds up what it reaches. What arrives is measured against the ground before the cleaned mod's changes, so ground and water lines
/// the cleaned mod made that stay count against it like any other change of the cleaned mod.
/// </summary>
/// <param name="Planned">The ground of the world after.</param>
/// <param name="Lowered">
/// Where the ground of the world after lies lower than the ground as it is: per cell and space on its land, a box from its lowest
/// height after to its highest now; empty where none goes down.
/// </param>
/// <param name="WithoutCleanedMod">The ground as it is with every height the cleaned mod changed at its height before the cleaned mod.</param>
/// <param name="WaterLinesThatStay">The water lines the cleaned mod moved that stay moved in the world after.</param>
public sealed record LandAfter(ITerrainHeights Planned, ImmutableArray<LoweredGround> Lowered, ITerrainHeights WithoutCleanedMod, ImmutableArray<MovedWaterLine> WaterLinesThatStay);

/// <param name="Box">Upright.</param>
public readonly record struct LoweredGround(RecordKey Space, OrientedBox Box);

/// <summary>
/// The heights between a cell's water level before the cleaned mod and the cleaned mod's; a cell without water at one of them counts as
/// water infinitely low there.
/// </summary>
/// <param name="Grid">The exterior cell's grid; null for an interior, which holds its whole space.</param>
public readonly record struct MovedWaterLine(RecordKey Space, (int X, int Y)? Grid, float Lower, float Upper);

/// <param name="Targets">Each target object the follow-up removed or restored, in target order.</param>
/// <param name="Copies">Each object a copy stands for that the follow-up took along, in id order.</param>
/// <param name="Others">Each judged object the follow-up left without support once, in id order.</param>
/// <param name="HeldUp">With KeepSupportingObject: each judged object that loses its support, which the objects under it keep, in id order.</param>
/// <param name="Arriving">
/// Each object that does not fit where it arrives: a target object restored on the working copy or by the follow-up's rounds, an
/// object of <see cref="ObjectsLeaving.Others"/> moved, or one of <see cref="ObjectsLeaving.ArrivingInAnotherShape"/>, measured
/// against the world before the run took objects of any plugin away, in which the objects of <see cref="ObjectsLeaving.Staying"/>,
/// <see cref="ObjectsLeaving.TargetsStaying"/> and <see cref="ObjectsLeaving.StayingInAnotherShape"/> went back too, on the ground
/// without the cleaned mod's changes, and judged again in the world the arrivals settled before it leave, on the ground and water of
/// <see cref="ObjectsLeaving.Land"/>; target objects in target order, then the others in id order.
/// </param>
public sealed record FollowUpOutcomes(
    ImmutableArray<FollowedTarget> Targets,
    ImmutableArray<FollowedCopy> Copies,
    ImmutableArray<FollowedOther> Others,
    ImmutableArray<HeldUpOther> HeldUp,
    ImmutableArray<ArrivingWhereItDoesNotFit> Arriving)
{
    public static FollowUpOutcomes None { get; } = new([], [], [], [], []);
}

/// <summary>Why an object does not fit where it arrives.</summary>
public enum ArrivalMisfit
{
    /// <summary>It loses its support there.</summary>
    LosesSupport,

    /// <summary>Ground of the world after rises into it there, which the ground before the cleaned mod's changes did not.</summary>
    InGround,

    /// <summary>It lies partly between the two levels of a water line the cleaned mod moved that stays.</summary>
    AtWaterLine,
}

/// <param name="Arriving">The object as a supporter: a target object or an object of any plugin.</param>
/// <param name="Outcome">
/// For an object of a plugin trusted in full that loses its support, with PullObjectToTheGround or RemoveFloatingObject: pulled down or
/// removed from where it arrives (its object at the position it arrives at, the only part of its placement a pull-down writes). Null for
/// any other object, which the follow-up may not change, and for one that does not fit for another reason, which no follow-up mends.
/// </param>
public sealed record ArrivingWhereItDoesNotFit(Supporter Arriving, ArrivalMisfit Misfit, OtherModsObjectOutcome? Outcome);

/// <param name="Object">Where it stands now.</param>
/// <param name="LostFrom">
/// What it went with: with EverythingTouchingIt every target object that left and every object of any plugin that left its place it
/// touched, target objects first; with OnlyWhatLosesItsSupport what held up part of it and left its place, largest share first.
/// </param>
public sealed record FollowedCopy(OtherObject Object, ImmutableArray<Supporter> LostFrom);

/// <param name="Decision">Removed or restored, by the when-removed table.</param>
/// <param name="LostFrom">
/// What it went with, in its round's order: the target objects that left their place and held its support or that it touched, the
/// one whose linked group it followed, or the object of any plugin moved or taken away that it rested on or touched (the main one).
/// </param>
public sealed record FollowedTarget(TargetId Target, Decision Decision, ImmutableArray<Supporter> LostFrom);

/// <param name="LostFrom">
/// What held up part of it before anything left and left its place: target objects that left, objects moved away, objects the
/// follow-up took away, and the terrain where the ground under it goes down; largest share first, then in supporter order.
/// </param>
public sealed record FollowedOther(OtherModsObjectOutcome Outcome, ImmutableArray<Supporter> LostFrom);

/// <param name="LostFrom">
/// What held up part of it and leaves its place: target objects, objects moved away, and the terrain where the ground under it goes
/// down; largest share first, then in supporter order.
/// </param>
public sealed record HeldUpOther(OtherObject Object, ImmutableArray<Supporter> LostFrom);
