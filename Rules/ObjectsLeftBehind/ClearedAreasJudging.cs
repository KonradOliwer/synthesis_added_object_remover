using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind;

/// <summary>Judges the areas of the targets on given decisions, over the shown targets where their rounds measure them.</summary>
/// <param name="targets">Every target, by id.</param>
/// <param name="spaces">Every space the scan read.</param>
/// <param name="caches">Index the shown targets; their shapes are read.</param>
internal sealed class ClearedAreasJudging(
    ImmutableArray<TargetObject> targets, IReadOnlyDictionary<RecordKey, SpaceFact> spaces, IRunCaches caches) : IClearedAreasJudging
{
    public IClearedAreas JudgedOn(IRemovalDecisions decisions, ClearedArea settings)
    {
        var shownTargets = caches.IndexShownTargets(decisions.TargetsAsTheRoundsMeasureThem(targets));
        return new ClearedAreaJudge(new AreaContents(targets, shownTargets, caches.Shapes), decisions, spaces, settings);
    }
}
