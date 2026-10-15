namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

/// <summary>The facts of why a target object is never removed; the wording is built in Report.</summary>
/// <param name="Link">The link that keeps the object; null for a teleport door itself and for a linked group.</param>
/// <param name="Keeper">The member of the linked group that must stay; set only for <see cref="KeepKind.LinkedGroup"/>.</param>
public sealed record KeepReason(KeepKind Kind, LinkFact? Link = null, GroupKeeper? Keeper = null);

/// <summary>The linked group member that must stay, with its own reason.</summary>
public sealed record GroupKeeper(RecordKey Key, string? EditorId, KeepReason Reason);
