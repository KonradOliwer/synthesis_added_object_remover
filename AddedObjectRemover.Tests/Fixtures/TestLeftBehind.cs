using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Left-behind rules over in-memory targets and other mods' objects in <see cref="TestTargets.Space"/>.</summary>
internal static class TestLeftBehind
{
    private static readonly ModKey ReachRecords = ModKey.FromNameAndExtension("Reach.esp");

    /// <param name="others">Other mods' objects of <see cref="TestTargets.Space"/>.</param>
    public static LeftBehindRule CreateRule(
        IReadOnlyList<TargetObject> targets,
        IBaseObjectShapes shapes,
        IReadOnlyList<OtherObject> others,
        LeftBehindOptions config)
    {
        var scene = TestScenes.Create(targets, others, shapes);
        var order = TargetWorkOrder.Of(targets);
        return new(
            targets,
            shapes,
            scene.VisibleTargets,
            Hosts.Find(targets, shapes, scene.ObjectsThatCanCauseRemovals(Replacements.None(others.Count), NpcHandling.OnlyWhenStuckInObject), order, new Execution(Environment.ProcessorCount)),
            new ReachRule(new BaseFactsReader(new SkyrimMod(ReachRecords, SkyrimRelease.SkyrimSE).ToImmutableLinkCache())),
            config,
            order);
    }

    /// <summary>The left-behind round applied to fresh removal decisions, so referenced objects are kept.</summary>
    public static LeftBehindResult DecideWithRemovalDecisions(LeftBehindResult evaluated, ObjectsToKeep protection, int targetCount) =>
        LeftBehindRound.WithVerdicts(evaluated, RemovalDecisions.Start(protection, targetCount).Apply(RoundKind.LeftBehind, LeftBehindRound.Proposals(evaluated)));
}
