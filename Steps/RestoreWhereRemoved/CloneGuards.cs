using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>Why a copy planned for a placed record going back is lost in a pass, so the record stays where it stands now.</summary>
/// <param name="Failure">Why the check named by <paramref name="By"/> stopped unexpectedly; null when it did not.</param>
internal readonly record struct CloneLoss(CloneRefusal By, string? Failure);

/// <summary>
/// The rules each copy is judged by in every pass, given what goes back in it, each where the copy's original stands now: as one of
/// the cleaned mod's own objects, it must not be too close to an object that can cause removals once those candidates are back
/// (<see cref="CloneTooCloseCheck"/>), and actors must not walk on it: no triangle of a navmesh the cleaned mod changed that stays
/// changed has its centre inside the copy's box. Each rule only ever finds more as the passes refuse more, except where a refused
/// candidate stops being a cause; a copy lost for such a cause stays lost, since no pass takes a refusal back. Thread-safe.
/// </summary>
/// <param name="createTooClose">Built the first time a copy is checked, since it reads the trust of every candidate.</param>
/// <param name="triangles">Each triangle of every navmesh the cleaned mod changed or added as it stands, with its navmesh and that navmesh's space, in key order of the navmeshes.</param>
internal sealed class CloneGuards(
    AfterRemovalWorld world,
    Func<CloneTooCloseCheck> createTooClose,
    ObjectsThatBlockActors blocking,
    ImmutableArray<(RecordKey Navmesh, RecordKey Space, Vector3 Centre)> triangles,
    Execution execution)
{
    private readonly ComputedOnce<CloneTooCloseCheck> _tooClose = new(createTooClose);

    /// <param name="createTooClose">Built the first time a copy is checked, since it reads the trust of every candidate.</param>
    /// <param name="changedNavmeshes">Every navmesh the cleaned mod changed or added.</param>
    public static CloneGuards Of(
        AfterRemovalWorld world,
        Func<CloneTooCloseCheck> createTooClose,
        ObjectsThatBlockActors blocking,
        IEnumerable<NavmeshChangedByCleanedMod> changedNavmeshes,
        ImmutableArray<NavmeshTriangleInCell> triangles,
        Execution execution)
    {
        var spaceOf = changedNavmeshes.ToDictionary(navmesh => navmesh.Navmesh, navmesh => navmesh.SpaceKey);
        return new CloneGuards(world, createTooClose, blocking, [.. triangles.Select(each => (each.Navmesh, spaceOf[each.Navmesh], each.Triangle.Centre))], execution);
    }

    /// <param name="goingBack">The indexes of the candidates going back.</param>
    /// <param name="withClone">Those of them going back with a copy.</param>
    /// <param name="staysChanged">The navmeshes that stay changed.</param>
    /// <returns>The candidates whose copy is lost, each with the first rule that finds it, too close first.</returns>
    public Dictionary<int, CloneLoss> FindLost(IReadOnlySet<int> goingBack, IReadOnlySet<int> withClone, IReadOnlySet<RecordKey> staysChanged)
    {
        if (withClone.Count == 0) return [];
        int[] candidates = [.. withClone.Order()];
        TargetObject[] clones = [.. world.ClonesOf(withClone).Select((clone, position) => clone with { Id = new TargetId(position) })];
        if (clones.Length != candidates.Length) throw new InvalidOperationException($"{candidates.Length} placed records go back with a copy, but {clones.Length} of them stand now.");
        var tooCloseChecks = _tooClose.Value.Check(clones, goingBack);
        var walkedOn = ParallelMap.Run(execution, clones.Length, position => ActorsWalkOn(clones[position], staysChanged), ParallelMap.AutomaticRangeSize);
        var lost = new Dictionary<int, CloneLoss>();
        for (var position = 0; position < clones.Length; position++)
        {
            if (LossOf(tooCloseChecks[position], walkedOn[position]) is { } loss) lost[candidates[position]] = loss;
        }
        return lost;
    }

    private static CloneLoss? LossOf(PlacementCheck tooClose, bool walkedOn)
    {
        if (tooClose.Clash is not null || tooClose.Failure is not null) return new CloneLoss(CloneRefusal.TooCloseAsCleanedModsObject, tooClose.Failure);
        return walkedOn ? new CloneLoss(CloneRefusal.ActorsWalkOnIt, Failure: null) : null;
    }

    private bool ActorsWalkOn(TargetObject clone, IReadOnlySet<RecordKey> staysChanged) =>
        blocking.SolidBoxOf(clone) is { } box && triangles.Any(each => each.Space == clone.SpaceKey && staysChanged.Contains(each.Navmesh) && box.Contains(each.Centre));
}
