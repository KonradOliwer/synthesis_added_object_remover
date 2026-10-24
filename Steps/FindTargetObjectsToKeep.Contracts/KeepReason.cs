namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

/// <summary>The facts of why a target object is never removed; the wording is built in Report.</summary>
/// <param name="Link">The link that keeps the object; null for a teleport door itself, a linked group, a script and an own location ref type.</param>
/// <param name="Keeper">The member of the linked group that must stay; set only for <see cref="KeepKind.LinkedGroup"/>.</param>
/// <param name="Script">What the object's script names; set only for <see cref="KeepKind.ScriptNamesQuestOrScene"/>.</param>
/// <param name="Resting">What rests on the object; set only for <see cref="KeepKind.OtherModsObjectRestsOnIt"/>.</param>
public sealed record KeepReason(KeepKind Kind, LinkFact? Link = null, GroupKeeper? Keeper = null, ScriptFinding? Script = null, RestingObject? Resting = null);

/// <summary>An object resting on a target object.</summary>
/// <param name="KeptFor">
/// For a target object resting on it, which is kept because something rests on that one in turn: what rests on that one; null when the
/// resting object is another mod's object.
/// </param>
public sealed record RestingObject(RecordKey Key, string? EditorId, RestingObject? KeptFor);

/// <summary>The linked group member that must stay, with its own reason.</summary>
public sealed record GroupKeeper(RecordKey Key, string? EditorId, KeepReason Reason);

/// <summary>The quest or scene a script names, and whether the script is on the object's base rather than on the object itself.</summary>
public sealed record ScriptFinding(ScriptNamedRecord Named, bool OnBase);
