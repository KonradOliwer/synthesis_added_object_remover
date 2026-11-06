using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind;

/// <summary>Judges the areas of the targets on given decisions, over the shown targets where their rounds measure them.</summary>
/// <param name="targets">Every target, by id.</param>
/// <param name="overriddenLater">The cleaned mod's objects later plugins hold that the areas count as standing, where they stand.</param>
/// <param name="spaces">Every space the scan read.</param>
/// <param name="caches">Index the shown targets; their shapes are read.</param>
internal sealed class ClearedAreasJudging(
    ImmutableArray<TargetObject> targets, ImmutableArray<TargetObject> overriddenLater, IReadOnlyDictionary<RecordKey, SpaceFact> spaces, IRunCaches caches)
    : IClearedAreasJudging
{
    /// <summary>The cleaned mod's objects later plugins hold, with the ids after the targets'.</summary>
    private readonly ImmutableArray<TargetObject> _overriddenLater =
        [.. overriddenLater.Select((overridden, index) => overridden with { Id = new TargetId(targets.Length + index) })];

    public IClearedAreas JudgedOn(IRemovalDecisions decisions, ClearedArea settings)
    {
        var shownTargets = caches.IndexShownTargets([.. decisions.TargetsAsTheRoundsMeasureThem(targets), .. _overriddenLater]);
        return new ClearedAreaJudge(new AreaContents(targets, _overriddenLater, shownTargets, caches.Shapes), decisions, spaces, settings);
    }
}
