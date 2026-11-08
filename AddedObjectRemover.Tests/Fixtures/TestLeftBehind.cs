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
    public static InvisibleLeftBehindResult PerformedWithoutFindings { get; } = new(StepOutcome.Performed, [], [], Failure: null);

    /// <summary>The invisible kinds the settings always keep.</summary>
    public static IKindsAlwaysKept KindsAlwaysKept(IBaseObjectShapes shapes, params InvisibleObjectKind[] kinds) =>
        LeftBehindRules.CreateInvisibleKindsAlwaysKept(shapes, new NeverRemovedInvisibleKinds(kinds.ToHashSet()));

    private static IBaseFacts ReachBases() => TestBaseFacts.Reader(new SkyrimMod(ReachRecords, SkyrimRelease.SkyrimSE).ToImmutableLinkCache());

    /// <param name="others">Other mods' objects of <see cref="TestTargets.Space"/>.</param>
    public static InvisibleLeftBehindRule CreateRule(
        IReadOnlyList<TargetObject> targets,
        IBaseObjectShapes shapes,
        IReadOnlyList<OtherObject> others,
        InvisibleLeftBehindSettings config,
        IKindsAlwaysKept? kindsAlwaysKept = null)
    {
        var scene = TestScenes.Create(targets, others, shapes);
        var order = TargetWorkOrder.Of(targets);
        return new(
            targets,
            shapes,
            scene.VisibleTargets,
            ContainingObjectFinder.Find(
                targets,
                shapes,
                scene.ObjectsThatCanCauseRemovals(TestReplacements.None(others.Count), NpcHandling.OnlyWhenStuckInsideTheObject),
                new HashSet<OtherId>(),
                order,
                new Execution(Environment.ProcessorCount)),
            LeftBehindRules.CreateInvisibleObjectReach(ReachBases()),
            LeftBehindRules.CreateSurroundingsJudging(),
            LeftBehindRules.CreateLeftBehindRoundJoin(),
            config,
            kindsAlwaysKept ?? KindsAlwaysKept(shapes),
            order);
    }

    /// <summary>The step over fresh removal decisions, so referenced objects are kept.</summary>
    public static InvisibleLeftBehindResult Find(
        IReadOnlyList<TargetObject> targets,
        IBaseObjectShapes shapes,
        IReadOnlyList<OtherObject> others,
        InvisibleLeftBehindSettings config,
        ObjectsToKeep protection,
        IKindsAlwaysKept? kindsAlwaysKept = null)
    {
        var scene = TestScenes.Create(targets, others, shapes);
        return InvisibleLeftBehindObjects.Find(
            new(
                [.. targets],
                scene.ObjectsThatCanCauseRemovals(TestReplacements.None(others.Count), NpcHandling.OnlyWhenStuckInsideTheObject),
                OtherModsObjectsResult.SwitchedOff,
                TestSeededDecisions.StartDisablingAll(protection, targets.Count),
                new TestRunCaches(
                    shapes,
                    bases: ReachBases(),
                    visibleTargets: scene.IndexVisibleTargets),
                kindsAlwaysKept ?? KindsAlwaysKept(shapes),
                LeftBehindRules.CreateInvisibleObjectReach(ReachBases()),
                LeftBehindRules.CreateSurroundingsJudging(),
                LeftBehindRules.CreateLeftBehindRoundJoin(),
                TargetWorkOrder.Of(targets),
                new Execution(Environment.ProcessorCount),
                config)).Result;
    }
}
