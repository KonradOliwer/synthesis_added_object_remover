using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

/// <summary>One visible target object checked for being left behind.</summary>
/// <param name="Kind">The object's kind when it is one of the listed kinds; null otherwise.</param>
/// <param name="KeepReason">Why a referenced object is kept; null otherwise.</param>
public sealed record VisibleLeftBehindCheck(
    int TargetIndex,
    VisibleObjectKind? Kind,
    SectorAreas Surroundings,
    VisibleLeftBehindOutcome Decision,
    KeepReason? KeepReason)
{
    public bool IsRemoved => VisibleLeftBehindOutcomes.IsRemoval(Decision);

    public bool IsRestored => VisibleLeftBehindOutcomes.IsRestoration(Decision);
}
