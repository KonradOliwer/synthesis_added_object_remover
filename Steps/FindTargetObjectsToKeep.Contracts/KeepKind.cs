namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

/// <summary>What makes a target object stay.</summary>
public enum KeepKind
{
    /// <summary>A teleport door, or the destination of one.</summary>
    TeleportDoor,

    /// <summary>A placed object that is not a checked target object links to it.</summary>
    PlacedReference,

    /// <summary>A quest, package, location, script, ... links to it.</summary>
    NonPlacedReference,

    /// <summary>Another member of its linked group must stay.</summary>
    LinkedGroup,

    /// <summary>An unexpected error stopped the check of what depends on it, and keeping an object is safe when that is unknown.</summary>
    CheckFailed,
}
