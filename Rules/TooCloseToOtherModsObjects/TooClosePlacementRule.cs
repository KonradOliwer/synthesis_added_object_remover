using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.RemovalRules.Contracts;

namespace AddedObjectRemover.Rules.TooCloseToOtherModsObjects;

/// <summary>
/// The too-close check of one target object at given placements, with the rule's zones and NPC rule. The search is built when the
/// first object is tried, so a run that asks nothing builds nothing.
/// </summary>
/// <param name="search">Null when no removal zone is on: the rule finds nothing.</param>
internal sealed class TooClosePlacementRule(ComputedOnce<ITooCloseSearch>? search) : IPlacementRule
{
    public IPlacementTries StartTries(TargetObject target) => new Tries(target, search?.Value.StartTries());

    /// <param name="tries">Null when no removal zone is on.</param>
    private sealed class Tries(TargetObject target, ITargetTries? tries) : IPlacementTries
    {
        public PlacementCheck Check(EulerPlacement placement)
        {
            if (tries is null) return default;
            var check = tries.Check(target.PlacedAt(placement));
            return new PlacementCheck(check.TooCloseTo?.ToReason(), check.Failure);
        }

        public void Dispose() => tries?.Dispose();
    }
}
