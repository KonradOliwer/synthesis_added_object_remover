namespace AddedObjectRemover;

public enum SpaceKind
{
    Worldspace,
    Interior,
}

/// <summary>What the plugins say about a space: its key, editor id and kind.</summary>
/// <param name="Name">The display name of the winning record, else its editor id; null when it has neither.</param>
public sealed record SpaceFact(RecordKey Key, string? EditorId, SpaceKind Kind, string? Name);

/// <summary>A cell as its record stores it: key and editor id.</summary>
public sealed record CellFact(RecordKey Key, string? EditorId);
