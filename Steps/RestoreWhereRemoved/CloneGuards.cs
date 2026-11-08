using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>Why a copy planned for a placed record going back is lost in a pass, so the record stays where it stands now.</summary>
/// <param name="Failure">Why the check named by <paramref name="By"/> stopped on a user-data failure; null when it did not.</param>
internal readonly record struct CloneLoss(CloneRefusal By, string? Failure);

/// <summary>
/// The rule each copy is judged by in every pass besides the too-close check (<see cref="StandingTooCloseChecks"/>), where the copy's
/// original stands now: actors must not walk on it, so no triangle of a navmesh the cleaned mod changed that stays changed has its
/// centre inside the copy's box. Thread-safe.
/// </summary>
/// <param name="triangles">Each triangle of every navmesh the cleaned mod changed or added as it stands, with its navmesh and that navmesh's space, in key order of the navmeshes.</param>
internal sealed class CloneGuards(
    AfterRemovalWorld world,
    ObjectsThatBlockActors blocking,
    ImmutableArray<(RecordKey Navmesh, RecordKey Space, Vector3 Centre)> triangles,
    Execution execution)
{
    /// <param name="changedNavmeshes">Every navmesh the cleaned mod changed or added.</param>
    public static CloneGuards Of(
        AfterRemovalWorld world,
        ObjectsThatBlockActors blocking,
        IEnumerable<NavmeshChangedByCleanedMod> changedNavmeshes,
        ImmutableArray<NavmeshTriangleInCell> triangles,
        Execution execution)
    {
        var spaceOf = changedNavmeshes.ToDictionary(navmesh => navmesh.Navmesh, navmesh => navmesh.SpaceKey);
        return new CloneGuards(world, blocking, [.. triangles.Select(each => (each.Navmesh, spaceOf[each.Navmesh], each.Triangle.Centre))], execution);
    }

    /// <param name="withClone">The candidates going back with a copy.</param>
    /// <param name="staysChanged">The navmeshes that stay changed.</param>
    /// <returns>The candidates whose copy actors walk on.</returns>
    public Dictionary<int, CloneLoss> FindLost(IReadOnlySet<int> withClone, IReadOnlySet<RecordKey> staysChanged)
    {
        if (withClone.Count == 0) return [];
        int[] candidates = [.. withClone.Order()];
        TargetObject[] clones = [.. world.ClonesOf(withClone)];
        if (clones.Length != candidates.Length) throw new InvalidOperationException($"{candidates.Length} placed records go back with a copy, but {clones.Length} of them stand now.");
        var walkedOn = ParallelMap.Run(execution, clones.Length, position => ActorsWalkOn(clones[position], staysChanged), ParallelMap.AutomaticRangeSize);
        return Enumerable.Range(0, clones.Length).Where(position => walkedOn[position]).ToDictionary(position => candidates[position], _ => new CloneLoss(CloneRefusal.ActorsWalkOnIt, Failure: null));
    }

    private bool ActorsWalkOn(TargetObject clone, IReadOnlySet<RecordKey> staysChanged) =>
        blocking.SolidBoxOf(clone) is { } box && triangles.Any(each => each.Space == clone.SpaceKey && staysChanged.Contains(each.Navmesh) && box.Contains(each.Centre));
}
