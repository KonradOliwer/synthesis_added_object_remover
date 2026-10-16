using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep;

/// <summary>
/// Finds, from the links into the target plugin, the target objects that stay removable but whose removal can still
/// harm the game, so the log can warn about them.
/// </summary>
internal static class RemovalRiskFinder
{
    public static RemovalRisks Find(IReadOnlyList<TargetObject> targets, IReadOnlyList<LinkFact> links) =>
        new(FindUniqueActorRoles(targets, links), FindLargeReferences(targets, links));

    /// <remarks>
    /// A Unique Actor alias names the NPC base, so it never keeps the placed NPC: without "Allow Disabled" the alias does
    /// not fill with a disabled actor, and a quest whose required alias cannot fill does not start.
    /// </remarks>
    private static ImmutableArray<UniqueActorRole> FindUniqueActorRoles(IReadOnlyList<TargetObject> targets, IReadOnlyList<LinkFact> links)
    {
        var aliasByNpc = new Dictionary<RecordKey, LinkFact>();
        foreach (var fact in links.Where(fact => fact.Relation == RelationKind.UniqueActor)) aliasByNpc.TryAdd(fact.Target, fact);
        return
        [
            .. from target in targets
               let npc = target.Base
               where npc is { Kind: BaseLinkKind.Npc } && aliasByNpc.ContainsKey(npc.Value.Record)
               select new UniqueActorRole(target.Id, aliasByNpc[npc.Value.Record]),
        ];
    }

    /// <remarks>
    /// A worldspace links placed objects only through its large-reference list. A disabled large reference can keep the
    /// object LOD of every large reference of its cell from being hidden, so LOD flickers or full models stop loading
    /// until the LOD is generated again.
    /// </remarks>
    private static ImmutableArray<TargetId> FindLargeReferences(IReadOnlyList<TargetObject> targets, IReadOnlyList<LinkFact> links)
    {
        var listed = links.Where(fact => fact.SourceIsWorldspace).Select(fact => fact.Target).ToHashSet();
        return [.. targets.Where(target => listed.Contains(target.Key)).Select(target => target.Id)];
    }
}
