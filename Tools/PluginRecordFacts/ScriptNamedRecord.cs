namespace AddedObjectRemover;

/// <summary>The record types a script property can name that hold the game's progress.</summary>
public enum ScriptNamedKind { Quest, Scene, Global }

/// <summary>A quest, scene or global variable that a property of a record's script names, as read.</summary>
public sealed record ScriptNamedRecord(RecordKey Key, string? EditorId, ScriptNamedKind Kind);
