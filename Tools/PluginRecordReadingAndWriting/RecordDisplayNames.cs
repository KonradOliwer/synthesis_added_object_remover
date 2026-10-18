using Mutagen.Bethesda.Strings;

namespace AddedObjectRemover;

/// <summary>The name a record shows in game, with its editor id standing in when it has none.</summary>
internal static class RecordDisplayNames
{
    /// <returns>The display name, else the editor id; null when neither has text.</returns>
    public static string? Of(ITranslatedStringGetter? name, string? editorId) =>
        name?.String is { } shown && !string.IsNullOrWhiteSpace(shown) ? shown
        : string.IsNullOrWhiteSpace(editorId) ? null
        : editorId;
}
