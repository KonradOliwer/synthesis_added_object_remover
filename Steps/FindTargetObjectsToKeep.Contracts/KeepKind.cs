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

    /// <summary>A script on it, or on its base object that the target plugin defines, names a quest or scene.</summary>
    ScriptNamesQuestOrScene,

    /// <summary>
    /// It is not a map marker and has a location ref type of its own (XLRT); a location listing it with one is a
    /// <see cref="NonPlacedReference"/>. A quest alias that picks a reference by that role does not fill with a disabled one.
    /// </summary>
    LocationRole,

    /// <summary>Another member of its linked group must stay.</summary>
    LinkedGroup,

    /// <summary>Another mod's object rests on it, or on a target object kept for this reason that rests on it.</summary>
    OtherModsObjectRestsOnIt,

    /// <summary>An unexpected error stopped the check of what depends on it, and keeping an object is safe when that is unknown.</summary>
    CheckFailed,
}
