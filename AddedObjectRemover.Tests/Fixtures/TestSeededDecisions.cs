namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Removal decisions seeded by a too-close round like the pipeline does, after a changed-terrain round when terrain seeds are given.</summary>
internal static class TestSeededDecisions
{
    private static readonly RemovalReason SeedReason = new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize());

    private static readonly RemovalReason TerrainSeedReason = new RemovalReason.TerrainChanged(new PluginName("Other.esp"), TerrainChangeKind.Height);

    private static readonly PluginName EarlierPlugin = new("Earlier.esp");

    /// <summary>No round has run; every removed object is disabled.</summary>
    public static RemovalDecisions StartDisablingAll(IObjectsToKeep protection, int targetCount) =>
        RemovalDecisions.Start(protection, [.. Enumerable.Repeat<WhenRemoved>(new WhenRemoved.Disable(), targetCount)]);

    public static RemovalDecisions Seed(ObjectsToKeep protection, int targetCount, IReadOnlyList<int> seeds, IReadOnlyList<int>? terrainSeeds = null) =>
        SeedFrom(StartDisablingAll(protection, targetCount), seeds, terrainSeeds);

    /// <summary>Like <see cref="Seed"/>, but the targets with an index in <paramref name="restoredTargets"/> go back to where they stood when a round removes them, instead of being disabled.</summary>
    public static RemovalDecisions SeedRestoring(ObjectsToKeep protection, IReadOnlyList<TargetObject> targets, IReadOnlyCollection<int> restoredTargets, IReadOnlyList<int> seeds) =>
        SeedFrom(
            RemovalDecisions.Start(
                protection,
                [.. targets.Select((target, index) => restoredTargets.Contains(index) ? WhenRestored(target) : new WhenRemoved.Disable())]),
            seeds,
            terrainSeeds: null);

    private static WhenRemoved WhenRestored(TargetObject target) =>
        new WhenRemoved.Restore(TestTargets.EarlierVersionAt(EarlierPlugin, target.Placement, target.Base));

    private static RemovalDecisions SeedFrom(RemovalDecisions started, IReadOnlyList<int> seeds, IReadOnlyList<int>? terrainSeeds)
    {
        var afterTerrain = terrainSeeds is null ? started : started.Apply(RoundKind.ChangedTerrain, Propose(terrainSeeds, TerrainSeedReason));
        return afterTerrain.Apply(RoundKind.TooClose, Propose(seeds, SeedReason));
    }

    private static ProposedRemoval[] Propose(IReadOnlyList<int> targets, RemovalReason reason) =>
        [.. targets.Select(target => new ProposedRemoval(new TargetId(target), reason))];
}
