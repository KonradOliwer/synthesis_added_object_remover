using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport;

/// <summary>The support test asked about objects that move away, in worlds of what stays (see <see cref="OtherModsObjects.FindHoldersOfObjectsThatStay"/>).</summary>
/// <param name="worldCaches">The object caches of the worlds asked about.</param>
internal sealed class HoldersOfObjectsThatStay(
    IObjectCachesOfWorlds worldCaches,
    ModTrustTable trust,
    LoadOrderPlugins plugins,
    OtherModsObjectsSettings settings,
    IBaseObjectShapes shapes,
    ITriangleMeshes triangles,
    ISurfaceSamples surfaces,
    ITerrainHeights terrain,
    Execution execution) : IHoldersOfObjectsThatStay
{
    public IHoldersSearch Over(CollectedObjects world, IReadOnlyList<TargetObject> standingNow) => new Search(this, world, standingNow);

    private IObjectCaches CachesOf(CollectedObjects world) => worldCaches.Of(world);

    private IReadOnlyDictionary<TargetId, OtherObject> Find(
        IReadOnlyList<TargetObject> standingNow,
        IVisibleTargetObjects standingIndex,
        Func<TargetId, bool> movesAway,
        IVisibleObjectsOfAnyPlugin staying,
        IReadOnlySet<OtherId> copiesOfMoving) =>
        OtherModsObjects.FindHoldersOfObjectsThatStay(
            standingNow, standingIndex, movesAway, staying, copiesOfMoving, trust, plugins, settings, shapes, triangles, surfaces, terrain, execution);

    /// <summary>The index of the objects standing now and the visible objects of the world are built on the first search, once.</summary>
    private sealed class Search(HoldersOfObjectsThatStay test, CollectedObjects world, IReadOnlyList<TargetObject> standingNow) : IHoldersSearch
    {
        private readonly ComputedOnce<IVisibleTargetObjects> _standingIndex = new(() => test.CachesOf(world).IndexVisibleTargets(standingNow));
        private readonly ComputedOnce<IVisibleObjectsOfAnyPlugin> _staying = new(() => test.CachesOf(world).VisibleObjectsOfAnyPlugin());

        public IReadOnlyDictionary<TargetId, OtherObject> Find(Func<TargetId, bool> movesAway, IReadOnlySet<OtherId> copiesOfMoving) =>
            test.Find(standingNow, _standingIndex.Value, movesAway, _staying.Value, copiesOfMoving);
    }
}
