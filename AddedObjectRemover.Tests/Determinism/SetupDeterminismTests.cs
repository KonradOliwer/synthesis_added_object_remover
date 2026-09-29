using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Determinism;

/// <summary>
/// The replaced-object matching and the host search give the same results with one and with
/// eight workers and in any work order, on a scene large enough to span many parallel ranges.
/// </summary>
public class SetupDeterminismTests
{
    private const int SequentialWorkers = 1;
    private const int ParallelWorkers = 8;
    private const int GridSize = 12;
    private const float GridSpacing = 60f;
    private const float ReplacementOffset = 4f;
    private const int ReplacedEvery = 3;
    private const int HostedEvery = 2;

    private static readonly ModKey Mod = ModKey.FromNameAndExtension("Setup.esp");

    private static readonly TestStatic Table = new(
        new FormKey(Mod, 0x701), @"test\table.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10))));

    private static readonly TestStatic Room = new(
        new FormKey(Mod, 0x702), @"test\room.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-25, -25, -5), new Vector3(25, 25, 60))));

    private static readonly ShapeCatalog Shapes = TestShapes.Create(Mod, "SetupDeterminismData", Table, Room);

    private sealed record Scene(List<TargetObject> Targets, List<ObjectVisibility> Visibility, List<OtherObject> Rivals);

    [Fact]
    public void ReplacementsMatchForOneAndEightWorkers()
    {
        var scene = CreateScene();
        var order = WorkOrder.Of(scene.Targets);

        var sequential = FindReplacements(scene, order, SequentialWorkers);
        var parallel = FindReplacements(scene, order, ParallelWorkers);

        Assert.NotEmpty(sequential);
        Assert.Equal(sequential, parallel);
    }

    [Fact]
    public void ReplacementsMatchForAReversedWorkOrder()
    {
        var scene = CreateScene();

        var expected = FindReplacements(scene, WorkOrder.Of(scene.Targets), ParallelWorkers);
        var reversed = FindReplacements(scene, Reversed(scene.Targets.Count), ParallelWorkers);

        Assert.Equal(expected, reversed);
    }

    [Fact]
    public void HostsMatchForOneAndEightWorkers()
    {
        var scene = CreateScene();
        var order = WorkOrder.Of(scene.Targets);

        var sequential = FindHosts(scene, order, SequentialWorkers);
        var parallel = FindHosts(scene, order, ParallelWorkers);

        Assert.Contains(sequential, host => host != null);
        Assert.Equal(sequential, parallel);
    }

    [Fact]
    public void HostsMatchForAReversedWorkOrder()
    {
        var scene = CreateScene();

        var expected = FindHosts(scene, WorkOrder.Of(scene.Targets), ParallelWorkers);
        var reversed = FindHosts(scene, Reversed(scene.Targets.Count), ParallelWorkers);

        Assert.Equal(expected, reversed);
    }

    /// <summary>A grid of visible tables, each third one shadowed by a rival table, and an invisible marker on each other cell, some inside a rival room.</summary>
    private static Scene CreateScene()
    {
        var targets = new List<TargetObject>();
        var visibility = new List<ObjectVisibility>();
        var rivals = new List<OtherObject>();
        for (var x = 0; x < GridSize; x++)
        {
            for (var y = 0; y < GridSize; y++)
            {
                var cell = x * GridSize + y;
                var position = new Vector3(x * GridSpacing, y * GridSpacing, 0);
                if (cell % HostedEvery == 0)
                {
                    targets.Add(TestTargets.Create(targets.Count, TestTargets.At(position), baseRef: null, TestTargets.Space));
                    visibility.Add(ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers));
                    if (cell % ReplacedEvery == 0) rivals.Add(TestShapes.Placed(Mod, rivals.Count, Room.Ref, position));
                    continue;
                }
                targets.Add(TestTargets.Create(targets.Count, TestTargets.At(position), Table.Ref, TestTargets.Space));
                visibility.Add(ObjectVisibility.Visible);
                if (cell % ReplacedEvery == 0) rivals.Add(TestShapes.Placed(Mod, rivals.Count, Table.Ref, position + new Vector3(ReplacementOffset, 0, 0)));
            }
        }
        return new Scene(targets, visibility, rivals);
    }

    private static List<Replacement> FindReplacements(Scene scene, WorkOrder order, int workers) =>
        [.. ReplacementMatcher.Find(
            scene.Targets,
            CreateWorld(scene, workers).RivalPositions(),
            scene.Rivals.Count,
            Shapes,
            order,
            Options(workers)).List];

    private static List<OtherId?> FindHosts(Scene scene, WorkOrder order, int workers)
    {
        var world = CreateWorld(scene, workers);
        var rivals = world.ActiveRivals(Replacements.None(scene.Rivals.Count), NpcHandling.OnlyWhenStuckInObject);
        var hosts = Hosts.Find(scene.Targets, scene.Visibility, rivals, order, Options(workers));
        return [.. Enumerable.Range(0, scene.Targets.Count).Select(index => hosts.HostOf(index)?.Id)];
    }

    private static AddedObjectRemover.Scene CreateWorld(Scene scene, int workers) =>
        TestScenes.Create(scene.Targets, scene.Rivals, Shapes, threads: workers);

    private static WorkOrder Reversed(int count) => new([.. Enumerable.Range(0, count).Reverse()]);

    private static ParallelOptions Options(int workers) => new() { MaxDegreeOfParallelism = workers };
}
