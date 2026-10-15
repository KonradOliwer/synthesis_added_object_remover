using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep;

/// <summary>Step 4: finds the target objects that must never be removed, from the links into the target plugin.</summary>
public static class ObjectsToKeepFinder
{
    public static ObjectsToKeepResult Find(IReadOnlyList<TargetObject> targets, IEnumerable<LinkFact> links)
    {
        var outcome = LinkRule.DecideLinkOutcome(targets, links);
        var objectsToKeep = ObjectsToKeep.Build(targets, outcome.GroupLinks, outcome.References);
        return new ObjectsToKeepResult(objectsToKeep, outcome.TargetObjectLinkCount, objectsToKeep.CheckFailures);
    }
}
