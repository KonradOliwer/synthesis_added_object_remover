using System.Collections.Immutable;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Scenes over in-memory target objects and other mods' objects.</summary>
internal static class TestScenes
{
    private static readonly ModKey NoRecords = ModKey.FromNameAndExtension("NoRecords.esp");

    /// <param name="rivals">Ids 0..n-1 in order.</param>
    /// <param name="backdrop">Ids following the rivals' in order; null when the backdrop is not collected.</param>
    /// <param name="bodies">Null when no placed NPC needs a body.</param>
    public static Scene Create(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<OtherObject> rivals,
        ShapeCatalog shapes,
        IReadOnlyList<OtherObject>? backdrop = null,
        NpcBodyCache? bodies = null,
        int threads = 1) =>
        Scene.Create(
            CreateWorld(targets, rivals, backdrop),
            shapes,
            new TriangleStore(shapes.ReadGeometry),
            bodies ?? CreateBodiesWithoutRecords(shapes),
            new Execution(threads),
            UntimedPhases.Instance);

    /// <summary>A scene whose only other objects are backdrop objects, as solids for support and obstacles.</summary>
    public static Scene CreateWithBackdrop(IReadOnlyList<TargetObject> targets, IReadOnlyList<OtherObject> backdrop, ShapeCatalog shapes, int threads = 1) =>
        Create(targets, [], shapes, backdrop, threads: threads);

    private static World CreateWorld(IReadOnlyList<TargetObject> targets, IReadOnlyList<OtherObject> rivals, IReadOnlyList<OtherObject>? backdrop) =>
        new(
            [.. targets],
            [.. rivals],
            backdrop is null ? Collected<ImmutableArray<OtherObject>>.NotCollected : Collected<ImmutableArray<OtherObject>>.Of([.. backdrop]),
            [],
            [.. TestTargets.References(targets.Count)],
            new Dictionary<FormKey, string>(),
            new ReadCounts(0, 0, 0, 0, 0, 0, 0),
            []);

    private static NpcBodyCache CreateBodiesWithoutRecords(ShapeCatalog shapes) =>
        new(new NpcBodyResolver(
            new SkyrimMod(NoRecords, SkyrimRelease.SkyrimSE).ToImmutableLinkCache(), shapes, new SkinnedBodyMeasurer(shapes.ReadGeometry)));
}
