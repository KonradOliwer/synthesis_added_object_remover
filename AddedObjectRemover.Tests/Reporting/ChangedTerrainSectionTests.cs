using System.Collections.Immutable;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

/// <summary>The changed-terrain section: its found line, the detailed counts line and the kept objects.</summary>
public sealed class ChangedTerrainSectionTests
{
    private const int TargetCount = 5;

    private static readonly PluginName Target = new("Target.esp");
    private static readonly TimeSpan Elapsed = TimeSpan.FromSeconds(1.5);
    private static readonly ReportContext Normal = new(null!, null!, Detailed: false);
    private static readonly ReportContext Detailed = new(null!, null!, Detailed: true);

    [Fact]
    public void TheNormalLogHasTheFoundLineAndTheKeptObjectsOnly()
    {
        var world = CreateWorld();

        var lines = LogSections.BuildChangedTerrainSection(world, Target, Found(), Kept(), Elapsed, Normal).Lines.ToArray();

        Assert.Equal(
            [
                "Found 3 of 5 Target.esp objects on terrain changed by other mods.",
                $"  Kept {RecordNames.Describe(world.Targets[4])} in {Describe.Space(world, TestTargets.Space)}: {TestKeepReasons.QuestDetail}.",
            ],
            lines);
    }

    [Fact]
    public void TheDetailedLogAddsTheTimeAndTheCountsByPlugin()
    {
        var lines = LogSections.BuildChangedTerrainSection(CreateWorld(), Target, Found(), [], Elapsed, Detailed).Lines.ToArray();

        Assert.Equal(
            [
                "Found 3 of 5 Target.esp objects on terrain changed by other mods in 1.5s.",
                "  Terrain: 12 cells compared; 3 objects on changed terrain (by changing mod: 2 B.esp, 1 A.esp); 7 objects not checked (interior, no terrain, or no trusted land record).",
            ],
            lines);
    }

    [Fact]
    public void WithNothingFoundTheDetailedCountsSayNone()
    {
        var found = ChangedTerrainResult.SwitchedOff with { Outcome = StepOutcome.Performed, CellsCompared = 2 };

        var lines = LogSections.BuildChangedTerrainSection(CreateWorld(), Target, found, [], Elapsed, Detailed).Lines;

        Assert.Equal("  Terrain: 2 cells compared; 0 objects on changed terrain (by changing mod: none); 0 objects not checked (interior, no terrain, or no trusted land record).", lines[1]);
    }

    private static ChangedTerrainResult Found()
    {
        ImmutableArray<ObjectOnChangedTerrain> hits = [Hit(0, "B.esp"), Hit(1, "A.esp"), Hit(2, "B.esp")];
        var proposals = hits.Select(hit => new ProposedRemoval(hit.Target, new RemovalReason.TerrainChanged(hit.ChangedBy, hit.Kind)));
        return new(StepOutcome.Performed, hits, [.. proposals], [], CellsCompared: 12, ObjectsWithoutTerrain: 7);
    }

    private static ImmutableArray<KeptObject> Kept() => [new KeptObject(4, TestKeepReasons.Quest, null)];

    private static ObjectOnChangedTerrain Hit(int index, string changedBy) => new(new TargetId(index), new PluginName(changedBy), TerrainChangeKind.Height);

    private static CollectedObjects CreateWorld()
    {
        var targets = TestTargets.CreateMany(TargetCount);
        return new CollectedObjects(
            [.. targets],
            [],
            [],
            [],
            [],
            new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, "Tamriel", SpaceKind.Worldspace, "Skyrim") },
            new ReadCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            [],
            TargetWorkOrder.Of(targets),
            null);
    }
}
