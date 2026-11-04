using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;

/// <summary>What a left-behind step judged about one target object; each step's check adds what only that step knows of the object, such as what it measured around it.</summary>
/// <param name="KeepReason">Why a referenced object is kept; null otherwise.</param>
public abstract record LeftBehindVerdict(int TargetIndex, LeftBehindOutcome Decision, KeepReason? KeepReason)
{
    public bool IsRemoved => LeftBehindOutcomes.IsRemoval(Decision);

    public bool IsRestored => LeftBehindOutcomes.IsRestoration(Decision);
}
