using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport;

/// <summary>
/// Whether a target object holds up another, by the drop test that finds the other mods' objects left without support
/// (<see cref="DropFinder"/>), in the world before any decision. Thread-safe; results depend only on the inputs.
/// </summary>
/// <param name="targets">Where they stand before any decision.</param>
/// <param name="visibleTargets">The index of <paramref name="targets"/>.</param>
internal sealed class TargetObjectsDropTest(
    ImmutableArray<TargetObject> targets,
    IVisibleTargetObjects visibleTargets,
    OtherModsObjectRules rules,
    DropFinder drops,
    IBaseObjectShapes shapes,
    Func<IVisibleObjectsOfAnyPlugin> objectsOfAnyPlugin,
    Execution execution) : ITargetObjectsDropTest
{
    public bool[] FindHoldingUp(IReadOnlyList<(TargetId Holder, TargetId Resting)> pairs)
    {
        var world = new SupportWorld(targets, visibleTargets, _ => true, objectsOfAnyPlugin(), SupportWorld.NoChanges, shapes);
        var rests = FindResting([.. pairs.Select(pair => pair.Resting).Distinct()], world);
        return ParallelMap.Run(
            execution,
            pairs.Count,
            () => new OtherObjectQueries(),
            (k, queries) => rests[pairs[k].Resting] && StopsRestingWithout(pairs[k], world, queries),
            ParallelMap.AutomaticRangeSize);
    }

    /// <returns>For each object: whether it rests on what is below it in the world.</returns>
    private Dictionary<TargetId, bool> FindResting(List<TargetId> resting, SupportWorld world)
    {
        var rests = ParallelMap.Run(
            execution,
            resting.Count,
            () => new OtherObjectQueries(),
            (k, queries) => drops.Rests(rules.StandingOf(targets[resting[k].Index]), world, queries),
            ParallelMap.AutomaticRangeSize);
        return resting.Zip(rests).ToDictionary(entry => entry.First, entry => entry.Second);
    }

    /// <summary>Whether the resting object no longer rests on what is below it without the holder.</summary>
    private bool StopsRestingWithout((TargetId Holder, TargetId Resting) pair, SupportWorld world, OtherObjectQueries queries) =>
        !drops.Rests(rules.StandingOf(targets[pair.Resting.Index]), world.Without([pair.Holder]), queries);
}
