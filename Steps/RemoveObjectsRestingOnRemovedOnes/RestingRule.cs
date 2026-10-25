using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The objects other target objects rest on, as the fit step asks, with EverythingTouchingIt and OnlyWhatLosesItsSupport alike:
/// an object holds up another target object when it holds at least the threshold share of that object's support, and that object
/// rests on what is below it and no longer does without it. An object kept shrunk or turned stays standing, so only what rests
/// on it from above could end up floating; objects that only touch it from the side or below do not count. With Nothing, no
/// object counts. Every other visible target object with a mesh counts, whether it may be removed, shrunk or turned or not, each
/// where it stands, so the answer depends only on the world.
/// </summary>
/// <param name="visibleTargets">The index of <paramref name="targets"/>.</param>
/// <param name="objectsOfAnyPlugin">Asked only when there is an object to check.</param>
internal sealed class RestingRule(
    ImmutableArray<TargetObject> targets,
    IVisibleTargetObjects visibleTargets,
    AlsoRemoveSettings settings,
    OtherModsObjectRules rules,
    IBaseObjectShapes shapes,
    ITriangleMeshes triangles,
    ISurfaceSamples surfaces,
    ITerrainHeights terrain,
    Func<IVisibleObjectsOfAnyPlugin> objectsOfAnyPlugin,
    Execution execution) : IRestingRule
{
    public ImmutableArray<TargetId> FindObjectsOthersRestOn(IReadOnlyList<TargetId> objects)
    {
        if (settings.Mode == FollowUpRemovalMode.Nothing || objects.Count == 0) return [];

        var checkedObjects = objects.Select(target => target.Index).Distinct().Order().ToList();
        var search = TouchSearch.Create(
            targets,
            checkedObjects.Select(index => targets[index].SpaceKey).ToHashSet(),
            excluded: [],
            shapes,
            triangles,
            settings.TouchGap,
            execution);
        var others = objectsOfAnyPlugin();
        var held = FindHoldingEnough(search, checkedObjects, others);
        return [.. FindHoldingUp(held, others).Distinct().Order().Select(index => new TargetId(index))];
    }

    /// <returns>(checked object, neighbour) pairs where the checked object holds at least the threshold share of the neighbour's support.</returns>
    private List<IndexPair> FindHoldingEnough(TouchSearch search, IReadOnlyList<int> checkedObjects, IVisibleObjectsOfAnyPlugin others)
    {
        var neighbours = search.FindNeighborsOfAll(checkedObjects);
        var contactsOf = FindContactsOf([.. neighbours.SelectMany(list => list).Distinct().Order()], search, others);
        return
        [
            .. checkedObjects.SelectMany((checkedObject, k) => neighbours[k]
                .Where(neighbour => HoldsEnoughOf(contactsOf[neighbour], checkedObject))
                .Select(neighbour => new IndexPair(checkedObject, neighbour))),
        ];
    }

    /// <returns>The checked objects of the pairs whose neighbour rests on what is below it only while the checked object stands.</returns>
    private IEnumerable<int> FindHoldingUp(List<IndexPair> held, IVisibleObjectsOfAnyPlugin others)
    {
        var world = new SupportWorld(targets, visibleTargets, _ => true, others, SupportWorld.NoChanges, shapes);
        var drops = new DropFinder(new SurfaceContacts(terrain, triangles, surfaces, settings.TouchGap), triangles, terrain, settings.TouchGap);
        var rests = FindResting([.. held.Select(pair => pair.Second).Distinct()], world, drops);
        var holdsUp = ParallelMap.Run(
            execution,
            held.Count,
            () => new OtherObjectQueries(),
            (k, queries) => rests[held[k].Second] && StopsRestingWithout(held[k], world, drops, queries),
            ParallelMap.AutomaticRangeSize);
        return held.Zip(holdsUp).Where(entry => entry.Second).Select(entry => entry.First.First);
    }

    /// <returns>For each neighbour: whether it rests on what is below it in the world.</returns>
    private Dictionary<int, bool> FindResting(List<int> neighbours, SupportWorld world, DropFinder drops)
    {
        var rests = ParallelMap.Run(
            execution,
            neighbours.Count,
            () => new OtherObjectQueries(),
            (k, queries) => drops.Rests(rules.StandingOf(targets[neighbours[k]]), world, queries),
            ParallelMap.AutomaticRangeSize);
        return neighbours.Zip(rests).ToDictionary(entry => entry.First, entry => entry.Second);
    }

    /// <summary>Whether the neighbour no longer rests on what is below it without the checked object.</summary>
    /// <param name="pair">The checked object first, then the neighbour that may rest on it.</param>
    private bool StopsRestingWithout(IndexPair pair, SupportWorld world, DropFinder drops, OtherObjectQueries queries) =>
        !drops.Rests(rules.StandingOf(targets[pair.Second]), world.Without([new TargetId(pair.First)]), queries);

    private Dictionary<int, CandidateContacts> FindContactsOf(List<int> resting, TouchSearch search, IVisibleObjectsOfAnyPlugin others)
    {
        var contactFinder = RestingObjects.CreateSupportContactFinder(targets, search, others, shapes, terrain, surfaces, settings.TouchGap);
        var found = contactFinder.FindContactsOfAll(resting, execution);
        return resting.Zip(found).ToDictionary(entry => entry.First, entry => entry.Second.Contacts);
    }

    private bool HoldsEnoughOf(CandidateContacts contacts, int supporter) =>
        SupportRule.FindSupportersHoldingEnough(contacts, settings.SupportLostFraction).Contains(Supporter.Target(supporter));
}
