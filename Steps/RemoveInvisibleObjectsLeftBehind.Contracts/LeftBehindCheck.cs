using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

/// <summary>One invisible target object checked for being left behind.</summary>
/// <param name="Radius">The search radius used: the configured one, or the object's own smaller reach.</param>
/// <param name="ContainingObject">The visible other-mod object it sits inside; null when none.</param>
/// <param name="KeepReason">Why a referenced object is kept; null otherwise.</param>
public sealed record LeftBehindCheck(
    int TargetIndex,
    InvisibleObjectKind Kind,
    float Radius,
    OtherObject? ContainingObject,
    SectorAreas Surroundings,
    LeftBehindOutcome Decision,
    KeepReason? KeepReason)
{
    public bool IsRemoved => LeftBehindOutcomes.IsRemoval(Decision);
}
