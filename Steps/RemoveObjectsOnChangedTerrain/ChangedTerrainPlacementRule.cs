using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>The changed-terrain check of one target object at another placement.</summary>
/// <param name="check">Null when the step is off: it finds nothing.</param>
internal sealed class ChangedTerrainPlacementRule(ImmutableArray<TargetObject> targets, ObjectTerrainCheck? check) : IPlacementRule
{
    public PlacementCheck Check(TargetId target, EulerPlacement placement)
    {
        if (check is null) return default;
        var found = check.Check(targets[target.Index].PlacedAt(placement));
        return new PlacementCheck(found.Hit is { } hit ? ChangedTerrainObjects.ReasonOf(hit) : null, found.Failure);
    }
}
