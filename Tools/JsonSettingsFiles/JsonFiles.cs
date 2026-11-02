using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AddedObjectRemover;

/// <summary>Parsing JSON text, and where in the text a value or a failure sits.</summary>
public static class JsonFiles
{
    /// <summary>Without this a comment ahead of the first value would be read as the file's value.</summary>
    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    /// <exception cref="JsonReaderException">The text is not valid JSON.</exception>
    public static JToken ParseIgnoringComments(string text) => JToken.Parse(text, IgnoreComments);

    public static int LineOf(JToken token) => ((IJsonLineInfo)token).LineNumber;

    /// <summary>Path is on <see cref="JsonSerializationException"/> and <see cref="JsonReaderException"/> individually, not on the shared base type.</summary>
    public static string? PathOf(JsonException exception) => exception switch
    {
        JsonSerializationException serialization => serialization.Path,
        JsonReaderException reader => reader.Path,
        _ => null,
    };

    /// <summary>Like the path, the line and position are on the two exception types individually; line 0 means unknown, which gives null.</summary>
    public static (int Line, int Position)? PositionOf(JsonException exception) => exception switch
    {
        JsonSerializationException { LineNumber: > 0 } serialization => (serialization.LineNumber, serialization.LinePosition),
        JsonReaderException { LineNumber: > 0 } reader => (reader.LineNumber, reader.LinePosition),
        _ => null,
    };
}
