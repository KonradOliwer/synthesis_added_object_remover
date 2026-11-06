using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>
/// The objects copies of the cleaned mod's content stand for that go with what leaves, by the rule the also-remove mode applies to
/// the cleaned mod's own objects: with EverythingTouchingIt every one touching a target object that left or an object of any plugin
/// leaving, where it stood; with OnlyWhatLosesItsSupport every one of whose support those hold at least the threshold share. An
/// object that stands after, moved or in another shape, counts only when it no longer touches the copy's object there. Thread-safe;
/// results depend only on the inputs.
/// </summary>
/// <param name="before">The world before anything leaves, in which the support is measured.</param>
/// <param name="inAnotherShape">The objects leaving with no place that stand after in another shape, as they stand then, by id.</param>
internal sealed class CopiesGoingWithWhatLeaves(
    SupportWorld before,
    OtherObjectSupport support,
    OtherModsObjectRules rules,
    ITriangleMeshes triangles,
    AlsoRemoveSettings settings,
    Execution execution,
    IReadOnlyDictionary<OtherId, OtherObject> inAnotherShape)
{
    private readonly PlacedMeshes _meshes = new(triangles, rules, settings.TouchGap, inAnotherShape);

    /// <param name="copies">The objects copies stand for, where they stand, in id order; each one that can rest on others.</param>
    /// <param name="targetsLeft">The target objects that left and could hold up others, where they stood, in target order.</param>
    /// <param name="leaving">The objects of any plugin leaving that can hold up others, in id order.</param>
    /// <returns>In id order.</returns>
    public ImmutableArray<FollowedCopy> Find(IReadOnlyList<OtherObject> copies, IReadOnlyList<TargetObject> targetsLeft, IReadOnlyList<OtherLeaving> leaving)
    {
        var wentWith = ParallelMap.Run(
            execution,
            copies.Count,
            () => (Touch: new TouchScratch(), Queries: new OtherObjectQueries()),
            (k, scratch) => WentWith(copies[k], targetsLeft, [.. leaving.Where(other => other.Object.Id != copies[k].Id)], scratch.Touch, scratch.Queries),
            ParallelMap.AutomaticRangeSize);
        return [.. copies.Zip(wentWith).Where(entry => !entry.Second.IsEmpty).Select(entry => new FollowedCopy(entry.First, entry.Second))];
    }

    /// <returns>What the copy's object went with; empty when it stays.</returns>
    private ImmutableArray<Supporter> WentWith(OtherObject copy, IReadOnlyList<TargetObject> targetsLeft, List<OtherLeaving> leaving, TouchScratch touch, OtherObjectQueries queries) =>
        settings.Mode switch
        {
            FollowUpRemovalMode.EverythingTouchingIt => TouchedThatLeft(copy, targetsLeft, leaving, touch),
            FollowUpRemovalMode.OnlyWhatLosesItsSupport => SupportThatLeft(copy, targetsLeft, leaving, touch, queries),
            _ => throw new UnreachableException($"Copies are judged with the follow-up removal mode {settings.Mode}."),
        };

    /// <returns>The target objects, then the objects of any plugin, that left and touched it.</returns>
    private ImmutableArray<Supporter> TouchedThatLeft(OtherObject copy, IReadOnlyList<TargetObject> targetsLeft, List<OtherLeaving> leaving, TouchScratch touch) =>
    [
        .. targetsLeft.Where(target => IsNear(copy, target.SpaceKey, rules.BoxOf(target)) && _meshes.Touch(copy, copy.Transform, target, touch))
            .Select(target => Supporter.Target(target.Id.Index)),
        .. leaving.Where(other => IsNear(copy, other.Object.SpaceKey, rules.BoxOf(other.Object, other.Object.Transform)) && _meshes.Loses(other, copy, touch))
            .Select(other => Supporter.Placed(other.Object.Id)),
    ];

    /// <returns>Empty when what left holds less than the threshold share of its support; otherwise what left and held it, largest share first.</returns>
    private ImmutableArray<Supporter> SupportThatLeft(OtherObject copy, IReadOnlyList<TargetObject> targetsLeft, List<OtherLeaving> leaving, TouchScratch touch, OtherObjectQueries queries)
    {
        var targetsGone = targetsLeft.Select(target => target.Id).ToHashSet();
        var othersGone = leaving.Where(other => !_meshes.StillTouches(other, copy, touch)).Select(other => other.Object.Id).ToHashSet();
        var contacts = support.FindContacts(rules.StandingOf(copy), before, queries);
        var gone = SupporterGone.ThatLeft(contacts, targetsGone.Contains, othersGone.Contains, isTerrainGone: false);
        if (gone.Count == 0) return [];
        return support.ReachesThreshold(gone.Sum(entry => entry.Weight) / contacts.TotalWeight, settings.SupportLostFraction)
            ? [.. gone.Select(entry => entry.Supporter)]
            : [];
    }

    /// <summary>The box comes within the touch gap of the copy's object's box.</summary>
    private bool IsNear(OtherObject copy, RecordKey space, OrientedBox box) =>
        space == copy.SpaceKey && box.WorldAabb(settings.TouchGap).Overlaps(rules.BoxOf(copy, copy.Transform).WorldAabb(0f));
}
