using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Caches.RunCaches;

/// <summary>
/// The object caches, indexed from the placed objects of one run, and the narrow views the steps read
/// over them. Every answer depends only on the world. Thread-safe.
/// </summary>
internal sealed class ObjectCaches : IObjectCaches
{
    private static readonly EqualTargets SameTargets = new();

    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly ComputedOnce<PlacedSpaces> _objectsOfAnyPlugin;
    private readonly ComputedOnce<IVisibleTargetObjects> _shownTargets;
    private readonly ObjectContainment _containment;
    private readonly IBaseObjectShapes _shapes;
    private readonly Execution _execution;
    private readonly ComputedOncePerKey<IReadOnlyList<TargetObject>, IVisibleTargetObjects> _indexes = new(Publication.BuiltOnce, SameTargets);
    private readonly ComputedOncePerKey<IReadOnlyList<TargetObject>, IVisibleTargetObjects> _shownIndexes = new(Publication.BuiltOnce, SameTargets);

    private ObjectCaches(
        CollectedObjects world,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        INpcBodies bodies,
        ObjectMeasurementRules measurementRules,
        PlacedNpcRules placedNpcRules,
        Execution execution)
    {
        _targets = world.Targets;
        _shapes = shapes;
        _execution = execution;
        _containment = new ObjectContainment(shapes, triangles);
        var otherModObjects = new PlacedSpaces(world.OtherModObjects, shapes, measurementRules.OtherModObjects, _containment, execution);
        OtherModObjects = otherModObjects;
        PlacedNpcs = new PlacedNpcsBySpace(otherModObjects, bodies, placedNpcRules, execution);
        _objectsOfAnyPlugin = new ComputedOnce<PlacedSpaces>(
            () => new PlacedSpaces([.. world.OtherModAndSupportOnlyObjects], shapes, measurementRules.ObjectsOfAnyPlugin, _containment, execution));
        VisibleTargets = new VisibleTargetObjects(world.Targets, shapes, _containment, execution, IsVisible);
        _shownTargets = new ComputedOnce<IVisibleTargetObjects>(() => new VisibleTargetObjects(world.Targets, shapes, _containment, execution, IsShown));
    }

    /// <param name="measurementRules">How the other-mod and any-plugin object caches measure an object; the rules belong to the select step.</param>
    /// <param name="placedNpcRules">How the spawning NPCs of a space pick, places and counts the placed NPCs; the rules belong to the too-close rule.</param>
    public static ObjectCaches Create(
        CollectedObjects world,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        INpcBodies bodies,
        ObjectMeasurementRules measurementRules,
        PlacedNpcRules placedNpcRules,
        Execution execution) =>
        new(world, shapes, triangles, bodies, measurementRules, placedNpcRules, execution);

    public IOtherModObjectsBySpace OtherModObjects { get; }

    public IPlacedNpcsBySpace PlacedNpcs { get; }

    public IObjectsOfAnyPluginBySpace ObjectsOfAnyPlugin => _objectsOfAnyPlugin.Value;

    public IVisibleTargetObjects VisibleTargets { get; }

    public IVisibleTargetObjects IndexVisibleTargets(IReadOnlyList<TargetObject> targets) => Index(targets, () => VisibleTargets, _indexes, IsVisible);

    public IVisibleTargetObjects IndexShownTargets(IReadOnlyList<TargetObject> targets) => Index(targets, () => _shownTargets.Value, _shownIndexes, IsShown);

    /// <param name="ofTheCollectedObjects">The index of the collected objects of that visibility.</param>
    /// <param name="built">The indexes built for other lists of that visibility.</param>
    /// <param name="counts">Whether a target of this visibility is indexed.</param>
    private IVisibleTargetObjects Index(
        IReadOnlyList<TargetObject> targets,
        Func<IVisibleTargetObjects> ofTheCollectedObjects,
        ComputedOncePerKey<IReadOnlyList<TargetObject>, IVisibleTargetObjects> built,
        Func<ObjectVisibility, bool> counts) =>
        AreTheCollectedObjects(targets) ? ofTheCollectedObjects() : built.Get(targets, () => new VisibleTargetObjects(targets, _shapes, _containment, _execution, counts));

    private static bool IsVisible(ObjectVisibility visibility) => visibility.IsVisible;

    private static bool IsShown(ObjectVisibility visibility) => visibility.IsShown;

    private bool AreTheCollectedObjects(IReadOnlyList<TargetObject> targets) => SameTargets.Equals(targets, _targets);

    public IVisibleObjectsOfAnyPlugin VisibleObjectsOfAnyPlugin() => new VisibleObjectsOfAnyPlugin(ObjectsOfAnyPlugin);

    public IVisibleObjectsOfAnyPlugin VisibleObjectsOfAnyPluginAsTheyStand(IReadOnlyDictionary<OtherId, OtherObject?> changed) =>
        new VisibleObjectsOfAnyPluginAsTheyStand(ObjectsOfAnyPlugin, changed, _shapes, _containment);

    /// <summary>Lists of the same target objects, in the same order and at the same placements.</summary>
    private sealed class EqualTargets : IEqualityComparer<IReadOnlyList<TargetObject>>
    {
        public bool Equals(IReadOnlyList<TargetObject>? first, IReadOnlyList<TargetObject>? second) =>
            ReferenceEquals(first, second) || (first is not null && second is not null && first.SequenceEqual(second));

        public int GetHashCode(IReadOnlyList<TargetObject> targets) => targets.Aggregate(targets.Count, (hash, target) => HashCode.Combine(hash, target));
    }
}
