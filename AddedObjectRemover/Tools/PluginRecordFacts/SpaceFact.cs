namespace AddedObjectRemover;

public enum SpaceKind
{
    Worldspace,
    Interior,
}

/// <summary>What the plugins say about a space: its key, editor id and kind.</summary>
public sealed record SpaceFact(RecordKey Key, string? EditorId, SpaceKind Kind);

/// <summary>A cell as its record stores it: key and editor id.</summary>
public sealed record CellFact(RecordKey Key, string? EditorId);
