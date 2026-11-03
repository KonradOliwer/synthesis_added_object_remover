using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;

/// <summary>How one of the cleaned mod's objects counts in the areas the step measures.</summary>
public enum ClearedAreaObjectKind
{
    /// <summary>Drawn with a solid shape: counts by number and by box volume.</summary>
    Drawn,

    /// <summary>Its mesh holds only effect shapes (fog, light rays): counts by number alone.</summary>
    EffectOnly,

    /// <summary>A marker, sound, light or trigger box: judged by its position, counted in no share.</summary>
    Invisible,
}

/// <summary>One of the cleaned mod's objects checked for standing in a cleared area.</summary>
/// <param name="ObjectsAround">The objects <paramref name="Area"/> counts, the object itself left out.</param>
/// <param name="Area">The share of the cleaned mod's drawn objects standing in the area around it, on the decisions of the earlier steps.</param>
/// <param name="KeepReason">Why a referenced object is kept; null otherwise.</param>
public sealed record ClearedAreaCheck(
    int TargetIndex,
    ClearedAreaObjectKind Kind,
    int ObjectsAround,
    AreaShare Area,
    LeftBehindOutcome Decision,
    KeepReason? KeepReason) : LeftBehindVerdict(TargetIndex, Decision, KeepReason);
