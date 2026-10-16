namespace AddedObjectRemover;

/// <summary>The record types a script property can name whose progress a script can advance.</summary>
public enum ScriptNamedKind { Quest, Scene }

/// <summary>A quest or scene that a property of a record's script names, as read.</summary>
public sealed record ScriptNamedRecord(RecordKey Key, string? EditorId, ScriptNamedKind Kind);
