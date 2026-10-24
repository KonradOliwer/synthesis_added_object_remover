using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep;

/// <summary>
/// Step 4: finds the target objects that must never be removed, from the links into the target plugin, the scripts and what
/// rests on them, and the target objects whose removal the log warns about.
/// </summary>
public static class ObjectsToKeepFinder
{
    /// <param name="restingOn">The target objects other mods' objects rest on, with what rests on each; empty unless they are kept.</param>
    public static ObjectsToKeepResult Find(
        IReadOnlyList<TargetObject> targets, IReadOnlyList<LinkFact> links, IBaseFacts bases, IReadOnlyDictionary<TargetId, RestingObject> restingOn)
    {
        var outcome = LinkRule.DecideLinkOutcome(targets, links);
        var objectsToKeep = ObjectsToKeep.Build(targets, outcome.GroupLinks, AddRestingObjects(outcome.References, restingOn), bases);
        return new ObjectsToKeepResult(objectsToKeep, outcome.TargetObjectLinkCount, objectsToKeep.CheckFailures, RemovalRiskFinder.Find(targets, links));
    }

    /// <summary>Another mod's object resting on a target object depends on it like a record linking to it; a link found first stays the reason.</summary>
    private static ImmutableArray<KeepReason?> AddRestingObjects(ImmutableArray<KeepReason?> references, IReadOnlyDictionary<TargetId, RestingObject> restingOn) =>
    [
        .. references.Select((reference, index) =>
            reference ?? (restingOn.TryGetValue(new TargetId(index), out var resting) ? new KeepReason(KeepKind.OtherModsObjectRestsOnIt, Resting: resting) : null)),
    ];
}
