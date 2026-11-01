using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The rules the guards of restored placed records ask, over in-memory scenes, built the way the runner builds them.</summary>
internal sealed record TestPlacedGuardRules(
    Func<CollectedObjects, IObjectCaches> IndexObjects,
    Func<CollectedObjects, IObjectCaches, IEnumerable<OtherId>, TooCloseSettings, IPlacementRule> TooClose,
    Func<IReadOnlyList<TargetObject>, IVisibleTargetObjects, Func<TargetId, bool>, IVisibleObjectsOfAnyPlugin, IReadOnlySet<OtherId>, IReadOnlyDictionary<TargetId, OtherObject>>
        FindHoldersOfObjectsThatStay,
    OtherModsObjectsSettings Support)
{
    /// <param name="support">The touch gap and lost share of the support test.</param>
    public static TestPlacedGuardRules Create(
        IBaseObjectShapes shapes, ModTrustTable trust, LoadOrderPlugins plugins, OtherModsObjectsSettings support, ITerrainHeights terrain, int threads)
    {
        var triangles = new TriangleStore(shapes.ReadTriangles);
        var surfaces = TestSurfaceSamples.Over(triangles);
        return new TestPlacedGuardRules(
            world => TestScenes.Index(world, shapes, threads),
            (world, caches, excluded, settings) => TooCloseObjects.CreatePlacementRule(
                world.Targets,
                caches.VisibleTargets,
                RemovalCauseSelector.SelectForOldPlace(caches.OtherModObjects, world.OtherModObjects.Length, excluded, settings.Npcs),
                caches.PlacedNpcs,
                shapes,
                triangles,
                settings),
            (holders, holderIndex, movesAway, staying, copiesOfMoving) => OtherModsObjects.FindHoldersOfObjectsThatStay(
                holders, holderIndex, movesAway, staying, copiesOfMoving, trust, plugins, support, shapes, triangles, surfaces, terrain, new Execution(threads)),
            support);
    }
}
