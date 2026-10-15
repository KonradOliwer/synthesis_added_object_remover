using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep;

/// <param name="GroupLinks">Links between two target objects, which put them in one linked group.</param>
/// <param name="References">By target index: why a record that is not a checked target object links to it; the first reason found.</param>
/// <param name="TargetObjectLinkCount">Links from target objects to any target-plugin record, before those not between two target objects are dropped.</param>
internal sealed record LinkOutcome(ImmutableArray<TargetLink> GroupLinks, ImmutableArray<KeepReason?> References, int TargetObjectLinkCount);

/// <summary>
/// Decides what the links into the target plugin mean. A link from a target object to another
/// target-plugin record joins a linked group, except one to a teleport destination, which keeps it;
/// any other link from another record keeps the linked target object. Links to the record itself and
/// base object links count for nothing. A worldspace links placed objects only through its
/// large-reference list, which only speeds up loading, so its links count for nothing too.
/// </summary>
internal static class LinkRule
{
    /// <remarks>The first link found keeps its reason, so the facts must come in a fixed order: placed records before non-placed ones.</remarks>
    public static LinkOutcome DecideLinkOutcome(IReadOnlyList<TargetObject> targets, IEnumerable<LinkFact> facts)
    {
        var idByKey = targets.ToDictionary(target => target.Key, target => target.Id);
        var groupLinks = ImmutableArray.CreateBuilder<TargetLink>();
        var reasons = new Dictionary<TargetId, KeepReason>();
        var targetObjectLinkCount = 0;

        foreach (var fact in facts)
        {
            if (fact.Relation == RelationKind.BaseObject || fact.Source == fact.Target || fact.SourceIsWorldspace) continue;
            if (fact.SourceIsPlaced && fact.Relation != RelationKind.TeleportDestination && idByKey.TryGetValue(fact.Source, out var from))
            {
                targetObjectLinkCount++;
                if (idByKey.TryGetValue(fact.Target, out var to)) groupLinks.Add(new TargetLink(from, to));
            }
            else if (idByKey.TryGetValue(fact.Target, out var kept))
            {
                reasons.TryAdd(kept, CreateReason(fact));
            }
        }
        return new LinkOutcome(
            groupLinks.ToImmutable(),
            [.. targets.Select(target => reasons.GetValueOrDefault(target.Id))],
            targetObjectLinkCount);
    }

    private static KeepReason CreateReason(LinkFact fact) =>
        fact.SourceIsPlaced
            ? new KeepReason(fact.Relation == RelationKind.TeleportDestination ? KeepKind.TeleportDoor : KeepKind.PlacedReference, fact)
            : new KeepReason(KeepKind.NonPlacedReference, fact);
}
