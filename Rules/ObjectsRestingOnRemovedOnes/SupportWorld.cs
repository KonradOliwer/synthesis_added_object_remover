using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>Reusable buffers of one worker thread for the questions about other mods' objects.</summary>
internal sealed class OtherObjectQueries
{
    public ObjectQueryScratch Query { get; } = new();
    public List<VisibleTargetNear> Targets { get; } = [];
}

/// <summary>
/// The world another mod's object is judged in: the visible target objects that stand, at their placement; the visible objects of
/// any plugin, some of which were moved or taken away; and its ground. Thread-safe while <paramref name="changed"/> does not change.
/// </summary>
/// <param name="targets">At the placement <paramref name="targetIndex"/> was built from.</param>
/// <param name="stands">Whether a target object is still in the world.</param>
/// <param name="changed">The objects of any plugin that were moved (their new placement) or taken away (null).</param>
/// <param name="added">
/// Objects that stand only in this world, in id order: none of <paramref name="others"/>, with ids after theirs, or one of them in
/// another shape, whose id <paramref name="changed"/> takes away.
/// </param>
internal sealed class SupportWorld(
    IReadOnlyList<TargetObject> targets,
    IVisibleTargetObjects targetIndex,
    Func<TargetId, bool> stands,
    IVisibleObjectsOfAnyPlugin others,
    IReadOnlyDictionary<OtherId, PlacedTransform?> changed,
    IBaseObjectShapes shapes,
    ITerrainHeights terrain,
    IReadOnlyList<OtherObject>? added = null)
{
    private readonly IReadOnlyList<OtherObject> _added = added ?? [];

    /// <summary>The ground of this world.</summary>
    public ITerrainHeights Terrain => terrain;

    /// <summary>The added objects' bounds by space and cell, built on the first question: a cleaned mod may hide many records.</summary>
    private BoxesByCell<RecordKey>? _addedByCell;

    /// <summary>No object of any plugin moved or taken away.</summary>
    public static readonly IReadOnlyDictionary<OtherId, PlacedTransform?> NoChanges = ImmutableDictionary<OtherId, PlacedTransform?>.Empty;

    /// <summary>The world with the objects of any plugin as <paramref name="changes"/> leaves them.</summary>
    public SupportWorld With(IReadOnlyDictionary<OtherId, PlacedTransform?> changes) => new(targets, targetIndex, stands, others, changes, shapes, terrain, _added);

    /// <summary>The world with <paramref name="standing"/> standing in it as well (see the class's added objects).</summary>
    public SupportWorld WithAdded(IReadOnlyList<OtherObject> standing) => new(targets, targetIndex, stands, others, changed, shapes, terrain, standing);

    /// <summary>The world with <paramref name="ground"/> as its terrain.</summary>
    public SupportWorld OnGround(ITerrainHeights ground) => new(targets, targetIndex, stands, others, changed, shapes, ground, _added);

    /// <summary>The world with the given target objects taken out as well.</summary>
    public SupportWorld Without(IReadOnlyCollection<TargetId> takenOut) =>
        new(targets, targetIndex, target => stands(target) && !takenOut.Contains(target), others, changed, shapes, terrain, _added);

    /// <summary>The objects with a mesh, other than <paramref name="self"/>, whose world bounds overlap <paramref name="area"/>.</summary>
    /// <returns>Target objects in id order, then objects of any plugin in id order, then the moved ones in id order, then the added ones.</returns>
    public List<MeshSupporter> FindSupporters(RecordKey space, Box area, Supporter self, OtherObjectQueries queries)
    {
        var found = FindTargetSupporters(space, area, self, queries);
        AddUnchangedOthers(space, area, self, queries.Query, found);
        AddMovedOthers(space, area, self, found);
        AddAddedOthers(space, area, self, found);
        return found;
    }

    private void AddAddedOthers(RecordKey space, Box area, Supporter self, List<MeshSupporter> found)
    {
        if (_added.Count == 0) return;
        var byCell = LazyInitializer.EnsureInitialized(ref _addedByCell, () => new BoxesByCell<RecordKey>([.. _added.Select(other => (other.SpaceKey, BoundsOf(other, other.Transform)))]));
        foreach (var other in byCell.SharingACellWith(space, area).Select(position => _added[position]).Where(other => Supporter.Placed(other.Id) != self))
        {
            AddWhereItOverlaps(space, area, other, other.Transform, found);
        }
    }

    private void AddWhereItOverlaps(RecordKey space, Box area, OtherObject other, PlacedTransform transform, List<MeshSupporter> found)
    {
        if (other.SpaceKey != space || shapes.Of(other.Base).MeshPath is not { } meshPath || !BoundsOf(other, transform).Overlaps(area)) return;
        found.Add(new MeshSupporter(Supporter.Placed(other.Id), transform, meshPath));
    }

    private Box BoundsOf(OtherObject other, PlacedTransform transform) => OrientedBox.FromLocal(shapes.Of(other.Base).Box, transform).WorldAabb(0f);

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
            AddWhereItOverlaps(space, area, others.Get(id), transform, found);
        }
    }
}
