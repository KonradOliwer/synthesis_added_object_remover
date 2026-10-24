using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// KeepSupportingObject: the target objects that hold up other mods' objects (<see cref="TargetsHoldingUpOtherModsObjects"/>),
/// each where it stands before any decision. Round by round, the target objects that hold up a target object found so are added
/// too, by the share of its support they hold, so the whole support under another mod's object stays. Each target object keeps
/// the first object found resting on it, in id order, so the result does not depend on the thread count.
/// </summary>
internal sealed class HeldUpTargetsFinder(
    ImmutableArray<TargetObject> targets,
    TargetsHoldingUpOtherModsObjects holding,
    IVisibleObjectsOfAnyPlugin objectsOfAnyPlugin,
    IBaseObjectShapes shapes,
    ITriangleMeshes triangles,
    ITerrainHeights terrain,
    OtherModsObjectsSettings settings,
    Execution execution)
{
    public Dictionary<TargetId, RestingObject> Find()
    {
        var restingOn = holding.Find(targets).ToDictionary(entry => entry.Key, entry => new RestingObject(entry.Value.Key, entry.Value.EditorId, KeptFor: null));
        AddTargetsBelow(restingOn);
        return restingOn;
    }

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
                foreach (var target in TargetsHoldingUpOtherModsObjects.FindTargetsHoldingEnough(found[k].Contacts, settings.SupportLostFraction))
                {
                    if (restingOn.TryAdd(target, new RestingObject(holder.Key, holder.EditorId, restingOn[holder.Id]))) added.Add(target.Index);
                }
            }
            frontier = [.. added.Order()];
        }
    }
}
