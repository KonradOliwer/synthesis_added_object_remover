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

        var lines = LogSections.BuildChangedTerrainSection(world, Target, Found(), StartDecisions(), Kept(), Elapsed, Normal).Lines.ToArray();

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
        var lines = LogSections.BuildChangedTerrainSection(CreateWorld(), Target, Found(), StartDecisions(), [], Elapsed, Detailed).Lines.ToArray();

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
        var found = new ChangedTerrainResult([Pass(AllTargets(), [], cellsCompared: 2, objectsWithoutTerrain: 0)]);

        var lines = LogSections.BuildChangedTerrainSection(CreateWorld(), Target, found, StartDecisions(), [], Elapsed, Detailed).Lines;

        Assert.Equal("  Terrain: 2 cells compared; 0 objects on changed terrain (by changing mod: none); 0 objects not checked (interior, no terrain, or no trusted land record).", lines[1]);
    }

    [Fact]
    public void EachSettingsSetHasItsOwnFoundLineAndTheStepsTimeEndsTheLastOne()
    {
        var found = new ChangedTerrainResult(
            [
                Pass([new TargetId(0), new TargetId(1), new TargetId(2)], [Hit(0, "B.esp"), Hit(1, "A.esp")], cellsCompared: 3, objectsWithoutTerrain: 0),
                Pass([new TargetId(3), new TargetId(4)], [Hit(3, "B.esp")], cellsCompared: 1, objectsWithoutTerrain: 1),
            ]);

        var normal = LogSections.BuildChangedTerrainSection(CreateWorld(), Target, found, StartDecisions(), [], Elapsed, Normal).Lines.ToArray();
        var detailed = LogSections.BuildChangedTerrainSection(CreateWorld(), Target, found, StartDecisions(), [], Elapsed, Detailed).Lines.ToArray();

        Assert.Equal(
            [
                "Found 2 of 3 Target.esp objects on terrain changed by other mods.",
                "Found 1 of 2 Target.esp objects on terrain changed by other mods.",
            ],
            normal);
        Assert.Equal(
            [
                "Found 2 of 3 Target.esp objects on terrain changed by other mods.",
                "  Terrain: 3 cells compared; 2 objects on changed terrain (by changing mod: 1 A.esp, 1 B.esp); 0 objects not checked (interior, no terrain, or no trusted land record).",
                "Found 1 of 2 Target.esp objects on terrain changed by other mods in 1.5s.",
                "  Terrain: 1 cells compared; 1 objects on changed terrain (by changing mod: 1 B.esp); 1 objects not checked (interior, no terrain, or no trusted land record).",
            ],
            detailed);
    }

    [Fact]
    public void ASwitchedOffSettingsSetPrintsNoLine()
    {
        var found = new ChangedTerrainResult([ChangedTerrainPass.SwitchedOff(AllTargets()), Pass(AllTargets(), [Hit(0, "B.esp")], cellsCompared: 1, objectsWithoutTerrain: 0)]);

        var lines = LogSections.BuildChangedTerrainSection(CreateWorld(), Target, found, StartDecisions(), [], Elapsed, Normal).Lines.ToArray();

        Assert.Equal(["Found 1 of 5 Target.esp objects on terrain changed by other mods."], lines);
    }

    [Fact]
    public void ASettingsSetOfObjectsOfOtherModsNamesThemWithoutAnOwnPartAndAMixedOneNamesBoth()
    {
        var world = CreateWorld(TargetOrigin.CleanedMod, TargetOrigin.CleanedMod, TargetOrigin.CleanedMod, TargetOrigin.Moved, TargetOrigin.Moved);
        var found = new ChangedTerrainResult(
            [
                Pass([new TargetId(3), new TargetId(4)], [Hit(3, "B.esp")], cellsCompared: 1, objectsWithoutTerrain: 0),
                Pass([new TargetId(2), new TargetId(3)], [Hit(2, "B.esp")], cellsCompared: 1, objectsWithoutTerrain: 0),
            ]);

        var lines = LogSections.BuildChangedTerrainSection(world, Target, found, StartDecisions(), [], Elapsed, Normal).Lines.ToArray();

        Assert.Equal(
            [
                "Found 1 of 2 objects of other mods that Target.esp moved on terrain changed by other mods.",
                "Found 1 of 2 objects (1 Target.esp objects and 1 objects of other mods that Target.esp moved) on terrain changed by other mods.",
            ],
            lines);
    }

    [Fact]
    public void TheFoundLineCountsTheFoundObjectsThatWereRestored()
    {
        var targets = TestTargets.CreateMany(TargetCount);
        var protection = ObjectsToKeep.Build(targets, [], TestTargets.References(TargetCount), TestBaseFacts.Unresolved);
        var decisions = TestSeededDecisions.SeedRestoring(protection, targets, restoredTargets: [0, 2], seeds: [0, 1, 2]);

        var lines = LogSections.BuildChangedTerrainSection(CreateWorld(), Target, Found(), decisions, [], Elapsed, Normal).Lines.ToArray();

        Assert.Equal(["Found 3 of 5 Target.esp objects on terrain changed by other mods; 2 of them restored."], lines);
    }

    private static RemovalDecisions StartDecisions() =>
        TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(TestTargets.CreateMany(TargetCount), [], TestTargets.References(TargetCount), TestBaseFacts.Unresolved), TargetCount);

    private static ChangedTerrainResult Found() =>
        new([Pass(AllTargets(), [Hit(0, "B.esp"), Hit(1, "A.esp"), Hit(2, "B.esp")], cellsCompared: 12, objectsWithoutTerrain: 7)]);

    private static ImmutableArray<TargetId> AllTargets() => [.. Enumerable.Range(0, TargetCount).Select(index => new TargetId(index))];

    private static ChangedTerrainPass Pass(ImmutableArray<TargetId> objects, ImmutableArray<ObjectOnChangedTerrain> hits, int cellsCompared, int objectsWithoutTerrain) =>
        new(objects, StepOutcome.Performed, hits, [.. hits.Select(hit => new ProposedRemoval(hit.Target, new RemovalReason.TerrainChanged(hit.ChangedBy, hit.Kind)))], [], cellsCompared, objectsWithoutTerrain);

    private static ImmutableArray<KeptObject> Kept() => [new KeptObject(4, TestKeepReasons.Quest, null)];

    private static ObjectOnChangedTerrain Hit(int index, string changedBy) => new(new TargetId(index), new PluginName(changedBy), TerrainChangeKind.Height);

    /// <param name="origins">The origin of each target object in order; the cleaned mod's for all when none is given.</param>
    private static CollectedObjects CreateWorld(params TargetOrigin[] origins)
    {
        var targets = TestTargets.CreateMany(TargetCount)
            .Select((target, index) => origins.Length == 0 ? target : target with { Origin = origins[index] })
            .ToList();
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
