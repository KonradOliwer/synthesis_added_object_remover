namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

/// <summary>The facts of why a target object is never removed; the wording is built in Report.</summary>
/// <param name="Link">The link that keeps the object; null for a teleport door itself, a linked group, a script and an own location ref type.</param>
/// <param name="Keeper">The member of the linked group that must stay; set only for <see cref="KeepKind.LinkedGroup"/>.</param>
/// <param name="Script">What the object's script names; set only for <see cref="KeepKind.ScriptNamesQuestSceneOrGlobal"/>.</param>
public sealed record KeepReason(KeepKind Kind, LinkFact? Link = null, GroupKeeper? Keeper = null, ScriptFinding? Script = null);

/// <summary>The linked group member that must stay, with its own reason.</summary>
public sealed record GroupKeeper(RecordKey Key, string? EditorId, KeepReason Reason);

/// <summary>The quest, scene or global variable a script names, and whether the script is on the object's base rather than on the object itself.</summary>
public sealed record ScriptFinding(ScriptNamedRecord Named, bool OnBase);
