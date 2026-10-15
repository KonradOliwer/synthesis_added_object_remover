namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Removal decisions seeded by a too-close round like the pipeline does, after a changed-terrain round when terrain seeds are given.</summary>
internal static class TestSeededDecisions
{
    private static readonly RemovalReason SeedReason = new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize());

    private static readonly RemovalReason TerrainSeedReason = new RemovalReason.TerrainChanged(new PluginName("Other.esp"), TerrainChangeKind.Height);

    public static RemovalDecisions Seed(ObjectsToKeep protection, int targetCount, IReadOnlyList<int> seeds, IReadOnlyList<int>? terrainSeeds = null)
    {
        var started = RemovalDecisions.Start(protection, targetCount);
        var afterTerrain = terrainSeeds is null ? started : started.Apply(RoundKind.ChangedTerrain, Propose(terrainSeeds, TerrainSeedReason));
        return afterTerrain.Apply(RoundKind.TooClose, Propose(seeds, SeedReason));
    }

    private static ProposedRemoval[] Propose(IReadOnlyList<int> targets, RemovalReason reason) =>
        [.. targets.Select(target => new ProposedRemoval(new TargetId(target), reason))];
}
