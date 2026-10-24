using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>
/// The too-close check of one target object at a given placement, with the step's zones and NPC rule. The search is built on the
/// first check, so a run that asks nothing builds nothing.
/// </summary>
/// <param name="search">Null when no removal zone is on: the step is switched off and finds nothing.</param>
internal sealed class TooClosePlacementRule(ImmutableArray<TargetObject> targets, ComputedOnce<ITooCloseSearch>? search) : IPlacementRule
{
    public PlacementCheck Check(TargetId target, EulerPlacement placement)
    {
        if (search is null) return default;
        var check = search.Value.CheckAlone(targets[target.Index].PlacedAt(placement));
        return new PlacementCheck(check.TooCloseTo is { } hit ? TooCloseObjects.ReasonOf(hit) : null, check.Failure);
    }
}
