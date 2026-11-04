using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;

/// <param name="ClearedArea">When the area around one of the cleaned mod's objects counts as cleared, so the object is removed.</param>
/// <param name="MinObjectsAround">The fewest of the cleaned mod's other objects the area counts, standing or not, for an object to be removed.</param>
public sealed record ObjectsInClearedAreasSettings(ClearedArea ClearedArea, int MinObjectsAround);

/// <summary>Why the step for objects in cleared areas is switched off.</summary>
public enum ObjectsInClearedAreasOff
{
    /// <summary>Its switch is off.</summary>
    Switch,

    /// <summary>The size limit is 100% or more: a choice of the method's design, not something the shares force.</summary>
    SizeLimit,

    /// <summary>The count limit is 100% or more.</summary>
    CountLimit,

    /// <summary>Both limits are 100% or more.</summary>
    BothLimits,
}
