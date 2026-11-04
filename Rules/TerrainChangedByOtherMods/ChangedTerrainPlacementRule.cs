using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods;

/// <summary>The changed-terrain check of one target object at given placements.</summary>
/// <param name="check">Null when the settings could find nothing: it finds nothing.</param>
internal sealed class ChangedTerrainPlacementRule(ObjectTerrainCheck? check) : IPlacementRule
{
    public IPlacementTries StartTries(TargetObject target) => new Tries(target, check);

    private sealed class Tries(TargetObject target, ObjectTerrainCheck? check) : IPlacementTries
    {
        public PlacementCheck Check(EulerPlacement placement)
        {
            if (check is null) return default;
            var found = check.Check(target.PlacedAt(placement));
            return new PlacementCheck(found.Hit is { } hit ? new TerrainChanged(hit.ChangedBy, hit.Kind) : null, found.Failure);
        }

        public void Dispose()
        {
        }
    }
}
