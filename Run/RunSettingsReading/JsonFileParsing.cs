using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AddedObjectRemover;

internal static class JsonFileParsing
{
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
            return JsonFiles.ParseIgnoringComments(text);
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
}
