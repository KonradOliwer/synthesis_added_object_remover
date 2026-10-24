using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// KeepSupportingObject: the target objects that hold up other mods' objects, each where it stands before any decision. A target
/// object holds up another object when it holds at least the threshold share of that object's support, the relation
/// <see cref="RestingRule"/> uses between target objects, and the other object rests on what is below it but would not without
/// those target objects. Round by round, the
/// target objects that hold up a target object found so are added too, so the whole support under another mod's object stays.
/// Each target object keeps the first object found resting on it, in id order, so the result does not depend on the thread count.
/// </summary>
internal sealed class HeldUpTargetsFinder(
    ImmutableArray<TargetObject> targets,
    SupportWorld world,
    IVisibleObjectsOfAnyPlugin objectsOfAnyPlugin,
    OtherModsObjectRules rules,
    JudgedObjectsNear near,
    OtherObjectSupport support,
    DropFinder drops,
    IBaseObjectShapes shapes,
    ITriangleMeshes triangles,
    ITerrainHeights terrain,
    OtherModsObjectsSettings settings,
    Execution execution)
{
    public Dictionary<TargetId, RestingObject> Find()
    {
        var restingOn = FindTargetsUnderOtherModsObjects();
        AddTargetsBelow(restingOn);
        return restingOn;
    }

    private Dictionary<TargetId, RestingObject> FindTargetsUnderOtherModsObjects()
    {
        var resting = FindJudgedObjectsTouchingTargets();
        var holders = ParallelMap.Run(execution, resting.Count, () => new OtherObjectQueries(), (k, queries) => FindTargetsHoldingUp(resting[k], queries), ParallelMap.AutomaticRangeSize);
        var restingOn = new Dictionary<TargetId, RestingObject>();
        foreach (var (other, holdersOfOther) in resting.Zip(holders))
        {
            foreach (var target in holdersOfOther) restingOn.TryAdd(target, new RestingObject(other.Key, other.EditorId, KeptFor: null));
        }
        return restingOn;
    }

    /// <returns>
    /// None for an object that does not rest on what is below it, which only touches things beside it, and for one that would
    /// still rest on what is below it without them: those target objects only stand on it or beside it.
    /// </returns>
    private List<TargetId> FindTargetsHoldingUp(OtherObject other, OtherObjectQueries queries)
    {
        List<TargetId> holders = [.. FindTargetsHoldingEnough(support.FindContacts(other, world, queries))];
        return holders.Count > 0 && drops.Rests(other, world, queries) && !drops.Rests(other, world.Without(holders), queries) ? holders : [];
    }

    /// <returns>The judged objects whose box comes within the gap of a visible target object's box, in id order.</returns>
    private List<OtherObject> FindJudgedObjectsTouchingTargets() =>
        near.FindTouchingAny([.. targets.Select(rules.SupportingBoxOf).OfType<BoxInSpace>()], isLeftOut: _ => false);

    /// <summary>Adds, round by round, the target objects that hold up a target object added in the round before.</summary>
    private void AddTargetsBelow(Dictionary<TargetId, RestingObject> restingOn)
    {
        if (restingOn.Count == 0) return;

        var search = TouchSearch.Create(
            targets, restingOn.Keys.Select(target => targets[target.Index].SpaceKey).ToHashSet(), excluded: [], shapes, triangles, settings.TouchGap, execution);
        var contactFinder = RestingObjects.CreateSupportContactFinder(targets, search, objectsOfAnyPlugin, shapes, terrain, settings.TouchGap);
        List<int> frontier = [.. restingOn.Keys.Select(target => target.Index).Order()];
        while (frontier.Count > 0)
        {
            var found = contactFinder.FindContactsOfAll(frontier, execution);
            var added = new List<int>();
            for (var k = 0; k < frontier.Count; k++)
            {
                var holder = targets[frontier[k]];
                foreach (var target in FindTargetsHoldingEnough(found[k].Contacts))
                {
                    if (restingOn.TryAdd(target, new RestingObject(holder.Key, holder.EditorId, restingOn[holder.Id]))) added.Add(target.Index);
                }
            }
            frontier = [.. added.Order()];
        }
    }

    private IEnumerable<TargetId> FindTargetsHoldingEnough(CandidateContacts contacts) =>
        SupportRule.FindSupportersHoldingEnough(contacts, settings.SupportLostFraction)
            .Where(supporter => supporter.Type == SupporterType.Target)
            .Select(supporter => new TargetId(supporter.Index));
}
