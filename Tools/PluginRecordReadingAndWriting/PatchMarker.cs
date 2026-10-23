namespace AddedObjectRemover;

/// <summary>The text of a plugin's header description that marks the plugin as this patcher's output.</summary>
internal static class PatchMarker
{
    /// <summary>The line break plugin editors write into a header description.</summary>
    private const string DescriptionLineBreak = "\r\n";

    internal const string Text = "Added Object Remover patch";

    /// <remarks>The marker may stand anywhere in the description.</remarks>
    public static bool IsIn(string? description) => description?.Contains(Text, StringComparison.Ordinal) == true;

    /// <summary>
    /// The description with the marker: another patcher's description stays and the marker goes on a new line after it; a
    /// description that already holds the marker is returned as it is.
    /// </summary>
    public static string AddTo(string? description)
    {
        if (IsIn(description)) return description!;
        return string.IsNullOrEmpty(description) ? Text : $"{description}{DescriptionLineBreak}{Text}";
    }
}
