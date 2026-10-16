using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep;

/// <summary>
/// Step 4: finds the target objects that must never be removed, from the links into the target plugin and the scripts,
/// and the target objects whose removal the log warns about.
/// </summary>
public static class ObjectsToKeepFinder
{
    public static ObjectsToKeepResult Find(IReadOnlyList<TargetObject> targets, IReadOnlyList<LinkFact> links, IBaseFacts bases)
    {
        var outcome = LinkRule.DecideLinkOutcome(targets, links);
        var objectsToKeep = ObjectsToKeep.Build(targets, outcome.GroupLinks, outcome.References, bases);
        return new ObjectsToKeepResult(objectsToKeep, outcome.TargetObjectLinkCount, objectsToKeep.CheckFailures, RemovalRiskFinder.Find(targets, links));
    }
}
