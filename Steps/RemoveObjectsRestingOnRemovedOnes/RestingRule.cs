using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The objects others rest on, by the relation the step's rounds use: with EverythingTouchingIt, another target object's mesh
/// touches the object's; with OnlyWhatLosesItsSupport, the object holds at least the threshold share of another target object's
/// support; with Nothing, no object counts. Every other visible target object with a mesh counts, whether it may be removed or
/// not, each where it stands, so the answer depends only on the world.
/// </summary>
/// <param name="objectsOfAnyPlugin">Asked only for OnlyWhatLosesItsSupport, when there is an object to check.</param>
internal sealed class RestingRule(
    ImmutableArray<TargetObject> targets,
    AlsoRemoveSettings settings,
    IBaseObjectShapes shapes,
    ITriangleMeshes triangles,
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
        var restedOn = settings.Mode switch
        {
            FollowUpRemovalMode.EverythingTouchingIt => FindTouched(search, checkedObjects),
            FollowUpRemovalMode.OnlyWhatLosesItsSupport => FindSupporting(search, checkedObjects),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {settings.Mode}."),
        };
        return [.. restedOn.Order().Select(index => new TargetId(index))];
    }

    /// <remarks>Each pair holds the neighbour first, so the tester stops at the first neighbour that touches an object.</remarks>
    private IEnumerable<int> FindTouched(TouchSearch search, IReadOnlyList<int> checkedObjects)
    {
        var pairs = search.CollectFrontierPairs(checkedObjects, skip: _ => false).Select(pair => new IndexPair(pair.Second, pair.First)).ToList();
        return search.Tester.FindFirstInContact(pairs, alsoWhenCentreEnclosed: false, execution).Found.Select(pair => pair.Second);
    }

    private IEnumerable<int> FindSupporting(TouchSearch search, IReadOnlyList<int> checkedObjects)
    {
        var neighbours = search.FindNeighborsOfAll(checkedObjects);
        var contactsOf = FindContactsOf([.. neighbours.SelectMany(list => list).Distinct().Order()], search);
        return checkedObjects.Where((checkedObject, k) => neighbours[k].Any(neighbour => HoldsEnoughOf(contactsOf[neighbour], checkedObject)));
    }

    private Dictionary<int, CandidateContacts> FindContactsOf(List<int> resting, TouchSearch search)
    {
        var contactFinder = RestingObjects.CreateSupportContactFinder(targets, search, objectsOfAnyPlugin(), shapes, terrain, settings.TouchGap);
        var found = contactFinder.FindContactsOfAll(resting, execution);
        return resting.Zip(found).ToDictionary(entry => entry.First, entry => entry.Second.Contacts);
    }

    private bool HoldsEnoughOf(CandidateContacts contacts, int supporter) =>
        SupportRule.FindSupportersHoldingEnough(contacts, settings.SupportLostFraction).Contains(Supporter.Target(supporter));
}
