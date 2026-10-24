using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>Reusable buffers of one worker thread for the questions about other mods' objects.</summary>
internal sealed class OtherObjectQueries
{
    public ObjectQueryScratch Query { get; } = new();
    public List<VisibleTargetNear> Targets { get; } = [];
}

/// <summary>
/// The world another mod's object is judged in: the visible target objects that stand, at their placement; the visible objects of
/// any plugin, some of which the step moved or took away; and the terrain. Thread-safe while <paramref name="changed"/> does not change.
/// </summary>
/// <param name="targets">At the placement <paramref name="targetIndex"/> was built from.</param>
/// <param name="stands">Whether a target object is still in the world.</param>
/// <param name="changed">The objects of any plugin the step moved (their new placement) or took away (null).</param>
internal sealed class SupportWorld(
    IReadOnlyList<TargetObject> targets,
    IVisibleTargetObjects targetIndex,
    Func<TargetId, bool> stands,
    IVisibleObjectsOfAnyPlugin others,
    IReadOnlyDictionary<OtherId, PlacedTransform?> changed,
    IBaseObjectShapes shapes)
{
    /// <summary>The world with the objects of any plugin as <paramref name="changes"/> leaves them.</summary>
    public SupportWorld With(IReadOnlyDictionary<OtherId, PlacedTransform?> changes) => new(targets, targetIndex, stands, others, changes, shapes);

    /// <summary>The world with the given target objects taken out as well.</summary>
    public SupportWorld Without(IReadOnlyCollection<TargetId> takenOut) =>
        new(targets, targetIndex, target => stands(target) && !takenOut.Contains(target), others, changed, shapes);

    /// <summary>The objects with a mesh, other than <paramref name="self"/>, whose world bounds overlap <paramref name="area"/>.</summary>
    /// <returns>Target objects in id order, then objects of any plugin in id order, then the moved ones in id order.</returns>
    public List<MeshSupporter> FindSupporters(RecordKey space, Box area, Supporter self, OtherObjectQueries queries)
    {
        var found = FindTargetSupporters(space, area, self, queries);
        AddUnchangedOthers(space, area, self, queries.Query, found);
        AddMovedOthers(space, area, self, found);
        return found;
    }

    private List<MeshSupporter> FindTargetSupporters(RecordKey space, Box area, Supporter self, OtherObjectQueries queries)
    {
        targetIndex.Around(space, area.Center, area.HalfExtents.Length(), queries.Targets);
        var found = new List<MeshSupporter>();
        foreach (var near in queries.Targets)
        {
            var target = targets[near.Target.Index];
            var supporter = Supporter.Target(near.Target.Index);
            if (supporter == self || !stands(near.Target) || !near.Box.WorldAabb(0f).Overlaps(area) || shapes.Of(target.Base).MeshPath is not { } meshPath) continue;
            found.Add(new MeshSupporter(supporter, target.Transform, meshPath));
        }
        return found;
    }

    private void AddUnchangedOthers(RecordKey space, Box area, Supporter self, ObjectQueryScratch scratch, List<MeshSupporter> found)
    {
        others.Overlapping(space, area, scratch, scratch.Others);
        foreach (var id in scratch.Others)
        {
            if (Supporter.Placed(id) == self || changed.ContainsKey(id)) continue;
            var other = others.Get(id);
            if (shapes.Of(other.Base).MeshPath is { } meshPath) found.Add(new MeshSupporter(Supporter.Placed(id), other.Transform, meshPath));
        }
    }

    private void AddMovedOthers(RecordKey space, Box area, Supporter self, List<MeshSupporter> found)
    {
        foreach (var (id, placement) in changed.OrderBy(entry => entry.Key.Index))
        {
            if (Supporter.Placed(id) == self || placement is not { } transform) continue;
            var other = others.Get(id);
            var shape = shapes.Of(other.Base);
            if (other.SpaceKey != space || shape.MeshPath is not { } meshPath || !OrientedBox.FromLocal(shape.Box, transform).WorldAabb(0f).Overlaps(area)) continue;
            found.Add(new MeshSupporter(Supporter.Placed(id), transform, meshPath));
        }
    }
}
