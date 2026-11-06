using System.Collections.Frozen;
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
/// any plugin, some of which were moved or taken away; and its ground. Thread-safe; the moved objects and the target objects taken
/// out are copied when the world is built, so a caller may change its own collections afterwards. A world made from another that
/// keeps its moved or added objects shares their index with it.
/// </summary>
internal sealed class SupportWorld
{
    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly IVisibleTargetObjects _targetIndex;
    private readonly Func<TargetId, bool> _stands;
    private readonly IVisibleObjectsOfAnyPlugin _others;
    private readonly IBaseObjectShapes _shapes;
    private readonly ITerrainHeights _terrain;
    private readonly FrozenDictionary<OtherId, PlacedTransform?> _changed;
    private readonly PlacedByCell _moved;
    private readonly IReadOnlyList<OtherObject> _added;
    private readonly PlacedByCell _addedByCell;

    /// <param name="targets">At the placement <paramref name="targetIndex"/> was built from.</param>
    /// <param name="stands">Whether a target object is still in the world.</param>
    /// <param name="changed">The objects of any plugin that were moved (their new placement) or taken away (null).</param>
    /// <param name="added">
    /// Objects that stand only in this world, in id order: none of <paramref name="others"/>, with ids after theirs, or one of them in
    /// another shape, whose id <paramref name="changed"/> takes away.
    /// </param>
    public SupportWorld(
        IReadOnlyList<TargetObject> targets,
        IVisibleTargetObjects targetIndex,
        Func<TargetId, bool> stands,
        IVisibleObjectsOfAnyPlugin others,
        IReadOnlyDictionary<OtherId, PlacedTransform?> changed,
        IBaseObjectShapes shapes,
        ITerrainHeights terrain,
        IReadOnlyList<OtherObject>? added = null)
    {
        _targets = targets;
        _targetIndex = targetIndex;
        _stands = stands;
        _others = others;
        _shapes = shapes;
        _terrain = terrain;
        _changed = changed.ToFrozenDictionary();
        _moved = MovedOf(_changed, others, shapes);
        _added = added ?? [];
        _addedByCell = AddedOf(_added, shapes);
    }

    private SupportWorld(SupportWorld from, Func<TargetId, bool> stands, ITerrainHeights terrain, IReadOnlyList<OtherObject>? added)
    {
        _targets = from._targets;
        _targetIndex = from._targetIndex;
        _stands = stands;
        _others = from._others;
        _shapes = from._shapes;
        _terrain = terrain;
        _changed = from._changed;
        _moved = from._moved;
        _added = added ?? from._added;
        _addedByCell = added is null ? from._addedByCell : AddedOf(added, from._shapes);
    }

    /// <summary>The ground of this world.</summary>
    public ITerrainHeights Terrain => _terrain;

    /// <summary>No object of any plugin moved or taken away.</summary>
    public static readonly IReadOnlyDictionary<OtherId, PlacedTransform?> NoChanges = ImmutableDictionary<OtherId, PlacedTransform?>.Empty;

    /// <summary>The world with the objects of any plugin as <paramref name="changes"/> leaves them.</summary>
    public SupportWorld With(IReadOnlyDictionary<OtherId, PlacedTransform?> changes) => new(_targets, _targetIndex, _stands, _others, changes, _shapes, _terrain, _added);

    /// <summary>The world with <paramref name="standing"/> standing in it as well (see the added objects).</summary>
    public SupportWorld WithAdded(IReadOnlyList<OtherObject> standing) => new(this, _stands, _terrain, standing);

    /// <summary>The world with <paramref name="ground"/> as its terrain.</summary>
    public SupportWorld OnGround(ITerrainHeights ground) => new(this, _stands, ground, added: null);

    /// <summary>The world with the given target objects taken out as well.</summary>
    public SupportWorld Without(IReadOnlyCollection<TargetId> takenOut)
    {
        var takenOutCopy = takenOut.ToFrozenSet();
        var stands = _stands;
        return new(this, target => stands(target) && !takenOutCopy.Contains(target), _terrain, added: null);
    }

    /// <summary>The objects with a mesh, other than <paramref name="self"/>, whose world bounds overlap <paramref name="area"/>.</summary>
    /// <returns>Target objects in id order, then objects of any plugin in id order, then the moved ones in id order, then the added ones.</returns>
    public List<MeshSupporter> FindSupporters(RecordKey space, Box area, Supporter self, OtherObjectQueries queries)
    {
        var found = FindTargetSupporters(space, area, self, queries);
        AddUnchangedOthers(space, area, self, queries.Query, found);
        if (_changed.Count > 0) _moved.AddOverlapping(space, area, self, _shapes, found);
        if (_added.Count > 0) _addedByCell.AddOverlapping(space, area, self, _shapes, found);
        return found;
    }

    /// <summary>The moved objects that stand: many objects may be moved, and a question only reaches those near it.</summary>
    private static PlacedByCell MovedOf(FrozenDictionary<OtherId, PlacedTransform?> changed, IVisibleObjectsOfAnyPlugin others, IBaseObjectShapes shapes) =>
        new(() => [.. changed.Where(entry => entry.Value is not null).OrderBy(entry => entry.Key.Index).Select(entry => (others.Get(entry.Key), entry.Value!.Value))], shapes);

    /// <summary>The added objects: a cleaned mod may hide many records.</summary>
    private static PlacedByCell AddedOf(IReadOnlyList<OtherObject> added, IBaseObjectShapes shapes) => new(() => [.. added.Select(other => (other, other.Transform))], shapes);

    private static Box BoundsOf(IBaseObjectShapes shapes, OtherObject other, PlacedTransform transform) =>
        OrientedBox.FromLocal(shapes.Of(other.Base).Box, transform).WorldAabb(0f);

    private List<MeshSupporter> FindTargetSupporters(RecordKey space, Box area, Supporter self, OtherObjectQueries queries)
    {
        _targetIndex.Around(space, area.Center, area.HalfExtents.Length(), queries.Targets);
        var found = new List<MeshSupporter>();
        foreach (var near in queries.Targets)
        {
            var target = _targets[near.Target.Index];
            var supporter = Supporter.Target(near.Target.Index);
            if (supporter == self || !_stands(near.Target) || !near.Box.WorldAabb(0f).Overlaps(area) || _shapes.Of(target.Base).MeshPath is not { } meshPath) continue;
            found.Add(new MeshSupporter(supporter, target.Transform, meshPath));
        }
        return found;
    }

    private void AddUnchangedOthers(RecordKey space, Box area, Supporter self, ObjectQueryScratch scratch, List<MeshSupporter> found)
    {
        _others.Overlapping(space, area, scratch, scratch.Others);
        foreach (var id in scratch.Others)
        {
            if (Supporter.Placed(id) == self || _changed.ContainsKey(id)) continue;
            var other = _others.Get(id);
            if (_shapes.Of(other.Base).MeshPath is { } meshPath) found.Add(new MeshSupporter(Supporter.Placed(id), other.Transform, meshPath));
        }
    }

    /// <summary>Objects each where it stands in a world, in id order, with their bounds by space and cell, built by one thread on the first question.</summary>
    /// <remarks>A box that overlaps an area shares a cell with it, so a question finds every object of them it overlaps, in the same order.</remarks>
    private sealed class PlacedByCell
    {
        private sealed record PlacedAndBounds(List<(OtherObject Object, PlacedTransform Transform)> Placed, BoxesByCell<RecordKey> ByCell);

        private readonly ComputedOnce<PlacedAndBounds> _index;

        /// <param name="inIdOrder">The objects, each at its placement in this world, in id order.</param>
        public PlacedByCell(Func<List<(OtherObject Object, PlacedTransform Transform)>> inIdOrder, IBaseObjectShapes shapes) =>
            _index = new(() => Build(inIdOrder(), shapes));

        /// <remarks>The index holds only the objects of the space asked for, each with its bounds where it stands.</remarks>
        public void AddOverlapping(RecordKey space, Box area, Supporter self, IBaseObjectShapes shapes, List<MeshSupporter> found)
        {
            var index = _index.Value;
            foreach (var position in index.ByCell.SharingACellWith(space, area))
            {
                var (other, transform) = index.Placed[position];
                var supporter = Supporter.Placed(other.Id);
                if (supporter == self || shapes.Of(other.Base).MeshPath is not { } meshPath || !index.ByCell.BoxAt(position).Overlaps(area)) continue;
                found.Add(new MeshSupporter(supporter, transform, meshPath));
            }
        }

        private static PlacedAndBounds Build(List<(OtherObject Object, PlacedTransform Transform)> placed, IBaseObjectShapes shapes) =>
            new(placed, new BoxesByCell<RecordKey>([.. placed.Select(entry => (entry.Object.SpaceKey, BoundsOf(shapes, entry.Object, entry.Transform)))]));
    }
}
