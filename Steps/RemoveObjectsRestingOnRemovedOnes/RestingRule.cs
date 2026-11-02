using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The objects other target objects rest on, with EverythingTouchingIt and OnlyWhatLosesItsSupport alike:
/// an object holds up another target object when it holds at least the threshold share of that object's support, and that object
/// rests on what is below it and no longer does without it (the drop test of <paramref name="input"/>). An object kept shrunk or
/// turned stays standing, so only what rests on it from above could end up floating; objects that only touch it from the side or
/// below do not count. With Nothing, no object counts. Every other visible target object with a mesh counts, whether it may be
/// removed, shrunk or turned or not, each where it stands, so the answer depends only on the world.
/// </summary>
internal sealed class RestingRule(RestingObjects.RestingRuleInput input) : IRestingRule
{
    private ImmutableArray<TargetObject> Targets => input.Targets;

    private AlsoRemoveSettings Settings => input.Settings;

    private IRunCaches Caches => input.Caches;

    private Execution Execution => input.Execution;

    public ImmutableArray<TargetId> FindObjectsOthersRestOn(IReadOnlyList<TargetId> objects)
    {
        if (Settings.Mode == FollowUpRemovalMode.Nothing || objects.Count == 0) return [];

        var checkedObjects = objects.Select(target => target.Index).Distinct().Order().ToList();
        var support = SupportSearch.Create(SupportReads.From(Targets, Caches, Execution), Caches.VisibleObjectsOfAnyPlugin, Settings.TouchGap);
        var held = FindHoldingEnough(support, checkedObjects);
        return [.. FindHoldingUp(held).Distinct().Order().Select(index => new TargetId(index))];
    }

    /// <returns>(checked object, neighbour) pairs where the checked object holds at least the threshold share of the neighbour's support.</returns>
    private List<IndexPair> FindHoldingEnough(SupportSearch support, IReadOnlyList<int> checkedObjects)
    {
        var neighbours = support.Search.FindNeighborsOfAll(checkedObjects);
        var contactsOf = FindContactsOf([.. neighbours.SelectMany(list => list).Distinct().Order()], support.Contacts);
        return
        [
            .. checkedObjects.SelectMany((checkedObject, k) => neighbours[k]
                .Where(neighbour => HoldsEnoughOf(contactsOf[neighbour], checkedObject))
                .Select(neighbour => new IndexPair(checkedObject, neighbour))),
        ];
    }

    /// <returns>The checked objects of the pairs whose neighbour rests on what is below it only while the checked object stands.</returns>
    private IEnumerable<int> FindHoldingUp(List<IndexPair> held)
    {
        var holdsUp = input.Drops.FindHoldingUp([.. held.Select(pair => (new TargetId(pair.First), new TargetId(pair.Second)))]);
        return held.Zip(holdsUp).Where(entry => entry.Second).Select(entry => entry.First.First);
    }

    private Dictionary<int, CandidateContacts> FindContactsOf(List<int> resting, SupportContactFinder contactFinder)
    {
        var found = contactFinder.FindContactsOfAll(resting, Execution);
        return resting.Zip(found).ToDictionary(entry => entry.First, entry => entry.Second.Contacts);
    }

    private bool HoldsEnoughOf(CandidateContacts contacts, int supporter) =>
        SupportRule.FindSupportersHoldingEnough(contacts, Settings.SupportLostFraction).Contains(Supporter.Target(supporter));
}
