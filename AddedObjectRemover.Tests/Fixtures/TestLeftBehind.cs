using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Left-behind rules over in-memory targets and other mods' objects in <see cref="TestTargets.Space"/>.</summary>
internal static class TestLeftBehind
{
    private static readonly ModKey ReachRecords = ModKey.FromNameAndExtension("Reach.esp");

    /// <summary>A performed step that found nothing.</summary>
    public static LeftBehindResult PerformedWithoutFindings { get; } = new(StepOutcome.Performed, [], [], Failure: null);

    /// <param name="others">Other mods' objects of <see cref="TestTargets.Space"/>.</param>
    public static LeftBehindRule CreateRule(
        IReadOnlyList<TargetObject> targets,
        IBaseObjectShapes shapes,
        IReadOnlyList<OtherObject> others,
        LeftBehindSettings config)
    {
        var scene = TestScenes.Create(targets, others, shapes);
        var order = TargetWorkOrder.Of(targets);
        return new(
            targets,
            shapes,
            scene.VisibleTargets,
            ContainingObjectFinder.Find(targets, shapes, scene.ObjectsThatCanCauseRemovals(Replacements.None(others.Count), NpcHandling.OnlyWhenStuckInObject), order, new Execution(Environment.ProcessorCount)),
            new ReachRule(new BaseFactsReader(new SkyrimMod(ReachRecords, SkyrimRelease.SkyrimSE).ToImmutableLinkCache())),
            config,
            order);
    }

    /// <summary>The step over fresh removal decisions, so referenced objects are kept.</summary>
    public static LeftBehindResult Find(
        IReadOnlyList<TargetObject> targets,
        IBaseObjectShapes shapes,
        IReadOnlyList<OtherObject> others,
        LeftBehindSettings config,
        ObjectsToKeep protection)
    {
        var scene = TestScenes.Create(targets, others, shapes);
        return LeftBehindObjects.Find(
            [.. targets],
            scene.ObjectsThatCanCauseRemovals(Replacements.None(others.Count), NpcHandling.OnlyWhenStuckInObject),
            RemovalDecisions.Start(protection, targets.Count),
            scene.VisibleTargets,
            new BaseFactsReader(new SkyrimMod(ReachRecords, SkyrimRelease.SkyrimSE).ToImmutableLinkCache()),
            shapes,
            TargetWorkOrder.Of(targets),
            new Execution(Environment.ProcessorCount),
            config);
    }
}
