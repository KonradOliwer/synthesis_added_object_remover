using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>
/// The target objects that go with the objects of any plugin leaving (removed, pulled down from where they stood, or moved), by
/// the rule the also-remove mode applies to any object whose support is removed. A target object loses a leaving object it touches
/// where the object stood, unless the object stands after, moved or in another shape, and still touches it there. With EverythingTouchingIt: every one
/// that loses such an object, named after the first of them in id order. With OnlyWhatLosesItsSupport: every one that loses such an
/// object and of whose support the removed target objects, the objects taken away and the moved objects it loses hold at least the
/// threshold share, named after the one of those holding the largest share. Only visible target objects with a mesh that an
/// also-remove round may still decide take part. Thread-safe; results depend only on the inputs.
/// </summary>
/// <param name="targets">Where the rounds measure them: an object kept shrunk or turned at its new placement, a restored one where the cleaned mod put it.</param>
/// <param name="visibleTargets">The index of <paramref name="targets"/>.</param>
/// <param name="world">Every target object where the rounds measure it, removed or not, and the objects of any plugin where they stood.</param>
/// <param name="inAnotherShape">The objects leaving with no place that stand after in another shape, as they stand then, by id.</param>
internal sealed class TargetsOnTakenAwayObjects(
    ImmutableArray<TargetObject> targets,
    IVisibleTargetObjects visibleTargets,
    SupportWorld world,
    OtherObjectSupport support,
    OtherModsObjectRules rules,
    ITriangleMeshes triangles,
    AlsoRemoveSettings settings,
    Execution execution,
    IReadOnlyDictionary<OtherId, OtherObject> inAnotherShape)
{
    /// <summary>A target object losing leaving objects, with the first of them in id order and those standing after that it loses.</summary>
    private readonly record struct LosingLeaving(TargetId Target, OtherId FirstLost, ImmutableHashSet<OtherId> MovedLost);

    private readonly PlacedMeshes _meshes = new(triangles, rules, settings.TouchGap, inAnotherShape);

    /// <param name="leaving">In id order; each in the world's objects of any plugin, where it stood.</param>
    /// <returns>In target order.</returns>
    public List<ProposedRemoval> Propose(IRemovalDecisions decisions, IReadOnlyList<OtherLeaving> leaving)
    {
        var losing = FindTargetsLosing(decisions, leaving);
        return settings.Mode switch
        {
            FollowUpRemovalMode.EverythingTouchingIt =>
                [.. losing.Select(entry => new ProposedRemoval(entry.Target, new TouchingAnotherModsObject(entry.FirstLost)))],
            FollowUpRemovalMode.OnlyWhatLosesItsSupport =>
                ProposeLosingSupport(decisions, losing, [.. leaving.Where(other => !_meshes.StandsAfter(other)).Select(other => other.Object.Id)]),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {settings.Mode}."),
        };
    }

    /// <returns>In target order.</returns>
    private List<LosingLeaving> FindTargetsLosing(IRemovalDecisions decisions, IReadOnlyList<OtherLeaving> leaving)
    {
        var pairs = FindPairsWithinTheGap(decisions, leaving);
        var lost = ParallelMap.Run(
            execution,
            pairs.Count,
            () => new TouchScratch(),
            (k, scratch) => Loses(pairs[k].Other, targets[pairs[k].Target.Index], scratch) ? pairs[k] : ((OtherLeaving Other, TargetId Target)?)null,
            ParallelMap.AutomaticRangeSize);
        return
        [
            .. lost.OfType<(OtherLeaving Other, TargetId Target)>()
                .GroupBy(pair => pair.Target)
                .Select(group => new LosingLeaving(
                    group.Key,
                    group.First().Other.Object.Id,
                    [.. group.Where(pair => _meshes.StandsAfter(pair.Other)).Select(pair => pair.Other.Object.Id)]))
                .OrderBy(entry => entry.Target.Index),
        ];
    }

    /// <returns>Each leaving object, in the given order, with the target objects whose box comes within the gap of its box where it stood.</returns>
    private List<(OtherLeaving Other, TargetId Target)> FindPairsWithinTheGap(IRemovalDecisions decisions, IReadOnlyList<OtherLeaving> leaving)
    {
        var pairs = new List<(OtherLeaving Other, TargetId Target)>();
        var near = new List<VisibleTargetNear>();
        foreach (var other in leaving)
        {
            var area = rules.BoxOf(other.Object, other.Object.Transform).WorldAabb(settings.TouchGap);
            visibleTargets.Around(other.Object.SpaceKey, area.Center, area.HalfExtents.Length(), near);
            foreach (var candidate in near)
            {
                if (decisions.MayStillBeRemoved(candidate.Target)
                    && candidate.Box.WorldAabb(0f).Overlaps(area)
                    && rules.SupportingBoxOf(targets[candidate.Target.Index]) is not null)
                {
                    pairs.Add((other, candidate.Target));
                }
            }
        }
        return pairs;
    }

    private bool Loses(OtherLeaving other, TargetObject target, TouchScratch scratch) => _meshes.Loses(other, target, scratch);

    /// <param name="takenAway">The leaving objects that do not stand after.</param>
    private List<ProposedRemoval> ProposeLosingSupport(IRemovalDecisions decisions, List<LosingLeaving> losing, HashSet<OtherId> takenAway) =>
    [
        .. ParallelMap.Run(
                execution,
                losing.Count,
                () => new OtherObjectQueries(),
                (k, queries) => ProposeIfSupportLost(
                    losing[k].Target,
                    support.FindContacts(rules.StandingOf(targets[losing[k].Target.Index]), world, queries),
                    decisions,
                    other => takenAway.Contains(other) || losing[k].MovedLost.Contains(other)),
                ParallelMap.AutomaticRangeSize)
            .OfType<ProposedRemoval>(),
    ];

    /// <param name="isLost">Whether the target object lost an object of any plugin.</param>
    /// <returns>Null when the share stays below the threshold, or when no object it lost held any of its support.</returns>
    private ProposedRemoval? ProposeIfSupportLost(TargetId target, CandidateContacts contacts, IRemovalDecisions decisions, Func<OtherId, bool> isLost)
    {
        List<SupporterWeight> heldByLost =
        [
            .. contacts.Supporters
                .Where(entry => entry.Supporter.Type == SupporterType.PlacedObject && isLost(new OtherId(entry.Supporter.Index)))
                .OrderByDescending(entry => entry.Weight)
                .ThenBy(entry => entry.Supporter.Index),
        ];
        if (heldByLost.Count == 0) return null;

        var share = ShareOf(contacts, supporter => SupporterGone.IsGone(supporter, decisions.HasLeftItsPlace, isLost, isTerrainGone: false));
        return support.ReachesThreshold(share, settings.SupportLostFraction)
            ? new ProposedRemoval(
                target,
                new LostSupportWithAnotherModsObject(
                    share, ShareOf(contacts, supporter => IsRestoredTarget(supporter, decisions)), new OtherId(heldByLost[0].Supporter.Index)))
            : null;
    }

    private static float ShareOf(CandidateContacts contacts, Func<Supporter, bool> isCounted) =>
        contacts.Supporters.Where(entry => isCounted(entry.Supporter)).Sum(entry => entry.Weight) / contacts.TotalWeight;

    private static bool IsRestoredTarget(Supporter supporter, IRemovalDecisions decisions) =>
        supporter.Type == SupporterType.Target && decisions.Of(new TargetId(supporter.Index)) is Decision.Restored;
}
