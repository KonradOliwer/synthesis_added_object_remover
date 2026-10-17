using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AddedObjectRemover;

internal static class JsonFileParsing
{
    /// <summary>Without this a comment ahead of the first value would be read as the file's value.</summary>
    private static readonly JsonLoadSettings IgnoreComments = new() { CommentHandling = CommentHandling.Ignore };

    /// <param name="kind">What the file is called in the message: "Group file" or "Config file".</param>
    public static string ReadText(string kind, string filePath)
    {
        try
        {
            return File.ReadAllText(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigFilesProblem($"{kind} {filePath} could not be read: {ex.Message}");
        }
    }

    /// <param name="kind">What the file is called in the message: "Group file" or "Config file".</param>
    public static JToken Parse(string kind, string filePath, string text)
    {
        try
        {
            return JToken.Parse(text, IgnoreComments);
        }
        catch (JsonReaderException ex)
        {
            throw new ConfigFilesProblem($"{kind} {filePath} is not valid JSON: {ex.Message}");
        }
    }

    public static string DescribeKind(JToken token) => token.Type switch
    {
        JTokenType.Object => "an object",
        JTokenType.Array => "a list",
        JTokenType.String => "text",
        JTokenType.Integer or JTokenType.Float => "a number",
        JTokenType.Boolean => "true or false",
        JTokenType.Null => "null",
        _ => token.Type.ToString(),
    };

    public static int LineOf(JToken token) => ((IJsonLineInfo)token).LineNumber;
}
