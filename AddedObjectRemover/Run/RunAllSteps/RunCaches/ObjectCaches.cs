using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps.RunCaches;

/// <summary>
/// The caches C9 to C12, indexed from the placed objects of one run, and the narrow views the steps read
/// over them. Every answer depends only on the world. Thread-safe.
/// </summary>
internal sealed class ObjectCaches : IObjectCaches
{
    private readonly ComputedOnce<PlacedSpaces> _objectsOfAnyPlugin;
    private readonly ObjectContainment _containment;

    private ObjectCaches(
        CollectedObjects world, IBaseObjectShapes shapes, ITriangleMeshes triangles, INpcBodies bodies, Action<UnexpectedError> reportUnexpected, Execution execution)
    {
        _containment = new ObjectContainment(shapes, triangles);
        var otherModObjects = new PlacedSpaces(world.OtherModObjects, shapes, ReportUnselectable(reportUnexpected), execution);
        OtherModObjects = otherModObjects;
        PlacedNpcs = new PlacedNpcsBySpace(otherModObjects, bodies, execution);
        _objectsOfAnyPlugin = new ComputedOnce<PlacedSpaces>(
            () => new PlacedSpaces([.. world.OtherModAndSupportOnlyObjects], shapes, onMeasureFailed: null, execution));
        VisibleTargets = new VisibleTargetObjects(world.Targets, shapes, _containment, execution);
    }

    /// <param name="reportUnexpected">Told about each other-mod object that cannot be measured; it counts as invisible, so it causes no removals.</param>
    public static ObjectCaches Create(
        CollectedObjects world, IBaseObjectShapes shapes, ITriangleMeshes triangles, INpcBodies bodies, Action<UnexpectedError> reportUnexpected, Execution execution) =>
        new(world, shapes, triangles, bodies, reportUnexpected, execution);

    private static Action<OtherObject, Exception> ReportUnselectable(Action<UnexpectedError> reportUnexpected) =>
        (other, ex) => reportUnexpected(ObjectErrors.ForOtherModObject(
            other, "selecting the objects that can cause removals", "They were not used to remove target objects.", ex));

    public IOtherModObjectsBySpace OtherModObjects { get; }

    public IPlacedNpcsBySpace PlacedNpcs { get; }

    public IObjectsOfAnyPluginBySpace ObjectsOfAnyPlugin => _objectsOfAnyPlugin.Value;

    public IVisibleTargetObjects VisibleTargets { get; }

    public IOtherModObjectPositions OtherModObjectPositions() => new OtherModObjectPositions(OtherModObjects);

    public IObjectsThatCanCauseRemovals ObjectsThatCanCauseRemovals(Replacements replaced, NpcHandling npcs) =>
        new ObjectsThatCanCauseRemovals(
            OtherModObjects,
            _containment,
            other => !replaced.IsReplaced(other.Id) && (npcs == NpcHandling.CountLikeObjects || !other.IsPlacedNpc));

    public INpcsThatCanSpawn NpcsThatCanSpawn(Replacements replaced) => new NpcsThatCanSpawn(PlacedNpcs, replaced);

    /// <summary>Only when the run collected the support-only objects.</summary>
    public IVisibleObjectsOfAnyPlugin VisibleObjectsOfAnyPlugin() => new VisibleObjectsOfAnyPlugin(ObjectsOfAnyPlugin, _containment);
}
