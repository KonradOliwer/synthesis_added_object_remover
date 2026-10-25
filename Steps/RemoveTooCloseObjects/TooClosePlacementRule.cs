using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>
/// The too-close check of one target object at given placements, with the step's zones and NPC rule. The search is built when the
/// first object is tried, so a run that asks nothing builds nothing.
/// </summary>
/// <param name="search">Null when no removal zone is on: the step is switched off and finds nothing.</param>
internal sealed class TooClosePlacementRule(ImmutableArray<TargetObject> targets, ComputedOnce<ITooCloseSearch>? search) : IPlacementRule
{
    public IPlacementTries StartTries(TargetId target) => new Tries(targets[target.Index], search?.Value.StartTries());

    /// <param name="tries">Null when the step is switched off.</param>
    private sealed class Tries(TargetObject target, ITargetTries? tries) : IPlacementTries
    {
        public PlacementCheck Check(EulerPlacement placement)
        {
            if (tries is null) return default;
            var check = tries.Check(target.PlacedAt(placement));
            return new PlacementCheck(check.TooCloseTo is { } hit ? TooCloseObjects.ReasonOf(hit) : null, check.Failure);
        }

        public void Dispose() => tries?.Dispose();
    }
}
