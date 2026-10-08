namespace AddedObjectRemover;

/// <summary>Record labels as log text.</summary>
public static class RecordLabels
{
    /// <summary>"EditorId [key]", or just the key when the record has no editor ID.</summary>
    public static string Of(RecordKey key, string? editorId) =>
        string.IsNullOrEmpty(editorId) ? key.ToString() : $"{editorId} [{key}]";
}
