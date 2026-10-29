using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>The changed-terrain check of one target object at given placements; each check is the step's own.</summary>
/// <param name="check">Null when the step is off: it finds nothing.</param>
internal sealed class ChangedTerrainPlacementRule(ObjectTerrainCheck? check) : IPlacementRule
{
    public IPlacementTries StartTries(TargetObject target) => new Tries(target, check);

    private sealed class Tries(TargetObject target, ObjectTerrainCheck? check) : IPlacementTries
    {
        public PlacementCheck Check(EulerPlacement placement)
        {
            if (check is null) return default;
            var found = check.Check(target.PlacedAt(placement));
            return new PlacementCheck(found.Hit is { } hit ? ChangedTerrainObjects.ReasonOf(hit) : null, found.Failure);
        }

        public void Dispose()
        {
        }
    }
}
