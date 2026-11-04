using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The cleared-area rule the runner hands the restore step: built over the same targets, spaces, decisions and caches.</summary>
internal static class TestClearedAreas
{
    /// <summary>A switched-off step for objects in cleared areas, with its also-remove follow-up switched off too.</summary>
    public static ObjectsInClearedAreasResult SwitchedOff { get; } = new(StepOutcome.SwitchedOff, [], [], TimeSpan.Zero, TestRestingObjects.SwitchedOff, Failure: null);

    public static IClearedAreas Of(CollectedObjects collected, IRemovalDecisions decisions, IRunCaches caches, WhereRemovedRestoreSettings settings) =>
        Of(collected.Targets, collected.Spaces, decisions, caches, settings.ClearedArea);

    public static IClearedAreas Of(
        ImmutableArray<TargetObject> targets, IReadOnlyDictionary<RecordKey, SpaceFact> spaces, IRemovalDecisions decisions, IRunCaches caches, ClearedArea settings) =>
        LeftBehindRules.CreateClearedAreasJudging(targets, spaces, caches).JudgedOn(decisions, settings);
}
