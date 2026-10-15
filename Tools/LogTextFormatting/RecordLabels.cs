namespace AddedObjectRemover;

/// <summary>Record labels as log text.</summary>
public static class RecordLabels
{
    /// <summary>"EditorId [key]", or just the key when the record has no editor ID.</summary>
    public static string Of(RecordKey key, string? editorId) => Of(key.ToString(), editorId);

    /// <summary>"Name [key]", or just the key when there is no name.</summary>
    public static string Of(string keyText, string? name) =>
        string.IsNullOrEmpty(name) ? keyText : $"{name} [{keyText}]";
}
