using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Scenes over in-memory target objects and other mods' objects.</summary>
internal static class TestScenes
{
    private static readonly ModKey NoRecords = ModKey.FromNameAndExtension("NoRecords.esp");

    /// <param name="otherModObjects">Ids 0..n-1 in order.</param>
    /// <param name="supportOnlyObjects">Ids following the other-mod objects' in order; null when the support-only objects are not collected.</param>
    /// <param name="bodies">Null when no placed NPC needs a body.</param>
    public static ObjectCaches Create(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<OtherObject> otherModObjects,
        IBaseObjectShapes shapes,
        IReadOnlyList<OtherObject>? supportOnlyObjects = null,
        NpcBodyCache? bodies = null,
        int threads = 1,
        Action<UnexpectedError>? reportUnexpected = null)
    {
        var world = CreateWorld(targets, otherModObjects, supportOnlyObjects);
        var triangles = new TriangleStore(shapes.ReadTriangles);
        return ObjectCaches.Create(
            world, shapes, triangles, bodies ?? CreateBodiesWithoutRecords(shapes), reportUnexpected ?? ThrowUnexpected, new Execution(threads));
    }

    private static void ThrowUnexpected(UnexpectedError error) => throw new InvalidOperationException(error.Message);

    /// <summary>A scene whose only other objects are support-only objects, as supporters and objects around a marker.</summary>
    public static ObjectCaches CreateWithSupportOnlyObjects(IReadOnlyList<TargetObject> targets, IReadOnlyList<OtherObject> supportOnlyObjects, IBaseObjectShapes shapes, int threads = 1) =>
        Create(targets, [], shapes, supportOnlyObjects, threads: threads);

    public static CollectedObjects CreateWorld(IReadOnlyList<TargetObject> targets, IReadOnlyList<OtherObject> otherModObjects, IReadOnlyList<OtherObject>? supportOnlyObjects = null) =>
        new(
            [.. targets],
            [.. otherModObjects],
            supportOnlyObjects?.ToImmutableArray(),
            [],
            new Dictionary<RecordKey, SpaceFact>(),
            new ReadCounts(0, 0, 0, 0, 0, 0),
            []);

    private static NpcBodyCache CreateBodiesWithoutRecords(IBaseObjectShapes shapes) =>
        TestNpcBodies.Create(
            new SkyrimMod(NoRecords, SkyrimRelease.SkyrimSE).ToImmutableLinkCache(), shapes);
}
