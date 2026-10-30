using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>
/// Seeded random worlds of overlapping blocks of three sizes in several spaces, some linked across spaces, some referenced, some
/// excluded from the touch search, with a few seeds.
/// </summary>
internal static class TestBlockWorlds
{
    public const int TargetCount = 60;
    public const float TouchDistance = 1f;
    public const float SupportThreshold = 0.5f;

    private const int SpaceCount = 4;
    private const int LinkCount = 8;
    private const int SeedCount = 3;
    private const int ExcludedOneIn = 9;
    private const int ReferencedOneIn = 12;
    private const float Reach = 160f;
    private const float SmallestHalfSize = 8f;
    private const float LargestHalfSize = 24f;

    private static readonly TestStatic[] Blocks =
    [
        Block(0x721, @"test\blockworld_small.nif", SmallestHalfSize),
        Block(0x722, @"test\blockworld_middle.nif", (SmallestHalfSize + LargestHalfSize) / 2),
        Block(0x723, @"test\blockworld_large.nif", LargestHalfSize),
    ];

    public static IBaseObjectShapes Shapes { get; } = TestShapes.Create(TestTargets.TargetMod, "BlockWorldData", Blocks);

    /// <param name="Excluded">Per target: whether it never takes part, for the touch search alone.</param>
    public sealed record World(List<TargetObject> Targets, ObjectsToKeep Protection, bool[] Excluded, List<int> Seeds);

    public static IEnumerable<World> Create(int seed, int count) => Enumerable.Range(0, count).Select(trial => Create(new Random(seed + trial)));

    public static OrientedBox MeasureBox(TargetObject target) => OrientedBox.FromLocal(Shapes.Of(target.Base).Box, target.Transform);

    private static World Create(Random random)
    {
        List<TargetObject> targets =
        [
            .. Enumerable.Range(0, TargetCount).Select(index => TestTargets.Create(
                index,
                TestTargets.At(TestMeshes.RandomVector(random, Reach), TestMeshes.RandomAngle(random)),
                Blocks[random.Next(Blocks.Length)].Base,
                TestTargets.SpaceKey(0x100 + (uint)random.Next(SpaceCount)))),
        ];
        var links = Enumerable.Range(0, LinkCount).Select(_ => TestTargets.Link(random.Next(TargetCount), random.Next(TargetCount))).Where(link => link.From != link.To).ToList();
        var referenced = Enumerable.Range(0, TargetCount).Where(_ => random.Next(ReferencedOneIn) == 0).ToDictionary(target => target, _ => TestKeepReasons.Quest);
        var protection = ObjectsToKeep.Build(targets, links, TestTargets.References(TargetCount, referenced), TestBaseFacts.Unresolved);
        var excluded = Enumerable.Range(0, TargetCount).Select(_ => random.Next(ExcludedOneIn) == 0).ToArray();
        var seeds = Enumerable.Range(0, TargetCount).OrderBy(_ => random.Next()).Take(SeedCount).Order().ToList();
        return new World(targets, protection, excluded, seeds);
    }

    private static TestStatic Block(uint id, string model, float halfSize) =>
        new(new FormKey(TestTargets.TargetMod, id), model, TestMeshes.BoxTriangles(new Box(new Vector3(-halfSize), new Vector3(halfSize))));
}
