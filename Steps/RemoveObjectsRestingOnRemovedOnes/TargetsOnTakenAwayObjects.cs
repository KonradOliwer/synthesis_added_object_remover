using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// The target objects that go with the other mods' objects taken away (removed, or pulled down from where they stood), by the
/// rule the also-remove mode applies to any object whose support is removed. With EverythingTouchingIt: every one whose mesh
/// touches such an object where it stood, named after the first of them in id order. With OnlyWhatLosesItsSupport: every one that
/// touches such an object and of whose support the removed target objects and the objects taken away hold at least the
/// threshold share, named after the object taken away holding the largest share. Only visible target objects with a mesh that an
/// also-remove round may still decide take part. Thread-safe; results depend only on the inputs.
/// </summary>
/// <param name="targets">Where the rounds measure them: an object kept shrunk or turned at its new placement, a restored one where the cleaned mod put it.</param>
/// <param name="visibleTargets">The index of <paramref name="targets"/>.</param>
/// <param name="world">Every target object where the rounds measure it, removed or not, and the objects of any plugin where they stood.</param>
internal sealed class TargetsOnTakenAwayObjects(
    ImmutableArray<TargetObject> targets,
    IVisibleTargetObjects visibleTargets,
    SupportWorld world,
    OtherObjectSupport support,
    OtherModsObjectRules rules,
    ITriangleMeshes triangles,
    AlsoRemoveSettings settings,
    Execution execution)
{
    /// <summary>A target object touching objects taken away, with the first of them in id order.</summary>
    private readonly record struct TouchingTakenAway(TargetId Target, OtherId FirstTouched);

    /// <param name="takenAway">In id order.</param>
    /// <returns>In target order.</returns>
    public List<ProposedRemoval> Propose(IRemovalDecisions decisions, IReadOnlyList<OtherObject> takenAway)
    {
        var touching = FindTargetsTouching(decisions, takenAway);
        return settings.Mode switch
        {
            FollowUpRemovalMode.EverythingTouchingIt =>
                [.. touching.Select(entry => new ProposedRemoval(entry.Target, new RemovalReason.TouchingAnotherModsObject(entry.FirstTouched)))],
            FollowUpRemovalMode.OnlyWhatLosesItsSupport => ProposeLosingSupport(decisions, touching, [.. takenAway.Select(other => other.Id)]),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {settings.Mode}."),
        };
    }

    /// <returns>In target order.</returns>
    private List<TouchingTakenAway> FindTargetsTouching(IRemovalDecisions decisions, IReadOnlyList<OtherObject> takenAway)
    {
        var pairs = FindPairsWithinTheGap(decisions, takenAway);
        var touching = ParallelMap.Run(
            execution,
            pairs.Count,
            () => new TouchScratch(),
            (k, scratch) => MeshesTouch(pairs[k].Other, targets[pairs[k].Target.Index], scratch) ? pairs[k] : ((OtherObject Other, TargetId Target)?)null,
            ParallelMap.AutomaticRangeSize);
        return
        [
            .. touching.OfType<(OtherObject Other, TargetId Target)>()
                .GroupBy(pair => pair.Target)
                .Select(group => new TouchingTakenAway(group.Key, group.First().Other.Id))
                .OrderBy(entry => entry.Target.Index),
        ];
    }

    /// <returns>Each object taken away, in the given order, with the target objects whose box comes within the gap of its box.</returns>
    private List<(OtherObject Other, TargetId Target)> FindPairsWithinTheGap(IRemovalDecisions decisions, IReadOnlyList<OtherObject> takenAway)
    {
        var pairs = new List<(OtherObject Other, TargetId Target)>();
        var near = new List<VisibleTargetNear>();
        foreach (var other in takenAway)
        {
            var area = rules.BoxOf(other, other.Transform).WorldAabb(settings.TouchGap);
            visibleTargets.Around(other.SpaceKey, area.Center, area.HalfExtents.Length(), near);
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

    private bool MeshesTouch(OtherObject other, TargetObject target, TouchScratch scratch)
    {
        using var otherMesh = triangles.Acquire(rules.MeshPathOf(other));
        using var targetMesh = triangles.Acquire(rules.MeshPathOf(target));
        return otherMesh.Value is { } otherTree
            && targetMesh.Value is { } targetTree
            && MeshContact.SurfacesTouch(otherTree, other.Transform, targetTree, target.Transform, settings.TouchGap, scratch);
    }

    private List<ProposedRemoval> ProposeLosingSupport(IRemovalDecisions decisions, List<TouchingTakenAway> touching, HashSet<OtherId> takenAway) =>
    [
        .. ParallelMap.Run(
                execution,
                touching.Count,
                () => new OtherObjectQueries(),
                (k, queries) => ProposeIfSupportLost(
                    touching[k].Target, support.FindContacts(rules.StandingOf(targets[touching[k].Target.Index]), world, queries), decisions, takenAway),
                ParallelMap.AutomaticRangeSize)
            .OfType<ProposedRemoval>(),
    ];

    /// <returns>Null when the share stays below the threshold, or when no object taken away held any of its support.</returns>
    private ProposedRemoval? ProposeIfSupportLost(TargetId target, CandidateContacts contacts, IRemovalDecisions decisions, HashSet<OtherId> takenAway)
    {
        List<SupporterWeight> heldByTakenAway =
        [
            .. contacts.Supporters
                .Where(entry => entry.Supporter.Type == SupporterType.PlacedObject && takenAway.Contains(new OtherId(entry.Supporter.Index)))
                .OrderByDescending(entry => entry.Weight)
                .ThenBy(entry => entry.Supporter.Index),
        ];
        if (heldByTakenAway.Count == 0) return null;

        var share = ShareOf(contacts, supporter => IsGone(supporter, decisions, takenAway));
        return SupportRule.ReachesThreshold(share, settings.SupportLostFraction)
            ? new ProposedRemoval(
                target,
                new RemovalReason.LostSupportWithAnotherModsObject(
                    share, ShareOf(contacts, supporter => IsRestoredTarget(supporter, decisions)), new OtherId(heldByTakenAway[0].Supporter.Index)))
            : null;
    }

    private static float ShareOf(CandidateContacts contacts, Func<Supporter, bool> isCounted) =>
        contacts.Supporters.Where(entry => isCounted(entry.Supporter)).Sum(entry => entry.Weight) / contacts.TotalWeight;

    private static bool IsRestoredTarget(Supporter supporter, IRemovalDecisions decisions) =>
        supporter.Type == SupporterType.Target && decisions.Of(new TargetId(supporter.Index)) is Decision.Restored;

    private static bool IsGone(Supporter supporter, IRemovalDecisions decisions, HashSet<OtherId> takenAway) => supporter.Type switch
    {
        SupporterType.Target => decisions.HasLeftItsPlace(new TargetId(supporter.Index)),
        SupporterType.PlacedObject => takenAway.Contains(new OtherId(supporter.Index)),
        SupporterType.Terrain => false,
        _ => throw new UnreachableException($"Unknown supporter type {supporter.Type}."),
    };
}
