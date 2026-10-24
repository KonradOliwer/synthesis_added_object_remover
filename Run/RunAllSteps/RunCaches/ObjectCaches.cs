using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>
/// The caches C9 to C12, indexed from the placed objects of one run, and the narrow views the steps read
/// over them. Every answer depends only on the world. Thread-safe.
/// </summary>
internal sealed class ObjectCaches : IObjectCaches
{
    private readonly ComputedOnce<PlacedSpaces> _objectsOfAnyPlugin;
    private readonly ObjectContainment _containment;
    private readonly IBaseObjectShapes _shapes;
    private readonly Execution _execution;

    private ObjectCaches(
        CollectedObjects world,
        IBaseObjectShapes shapes,
        ITriangleMeshes triangles,
        INpcBodies bodies,
        ObjectMeasurementRules measurementRules,
        PlacedNpcRules placedNpcRules,
        Execution execution)
    {
        _shapes = shapes;
        _execution = execution;
        _containment = new ObjectContainment(shapes, triangles);
        var otherModObjects = new PlacedSpaces(world.OtherModObjects, shapes, measurementRules.OtherModObjects, _containment, execution);
        OtherModObjects = otherModObjects;
        PlacedNpcs = new PlacedNpcsBySpace(otherModObjects, bodies, placedNpcRules, execution);
        _objectsOfAnyPlugin = new ComputedOnce<PlacedSpaces>(
            () => new PlacedSpaces([.. world.OtherModAndSupportOnlyObjects], shapes, measurementRules.ObjectsOfAnyPlugin, _containment, execution));
        VisibleTargets = new VisibleTargetObjects(world.Targets, shapes, _containment, execution);
    }

    /// <param name="measurementRules">How C9 and C11 measure an object; the rules belong to the select step.</param>
    /// <param name="placedNpcRules">How C10 picks, places and counts the placed NPCs; the rules belong to the too-close step.</param>
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

    public IVisibleTargetObjects IndexVisibleTargets(IReadOnlyList<TargetObject> targets) => new VisibleTargetObjects(targets, _shapes, _containment, _execution);

    public IVisibleObjectsOfAnyPlugin VisibleObjectsOfAnyPlugin() => new VisibleObjectsOfAnyPlugin(ObjectsOfAnyPlugin);

    public IVisibleObjectsOfAnyPlugin VisibleObjectsOfAnyPluginAsTheyStand(IReadOnlyDictionary<OtherId, OtherObject?> changed) =>
        new VisibleObjectsOfAnyPluginAsTheyStand(ObjectsOfAnyPlugin, changed, _shapes, _containment);
}
