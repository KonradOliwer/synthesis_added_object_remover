using System.Text;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>The rows of a CSV report file, each by its column names.</summary>
internal static class ReportRows
{
    private const char Separator = ',';
    private const char Quote = '"';

    /// <remarks>Every row must have a field for each column, so a shifted column fails here.</remarks>
    public static List<Dictionary<string, string>> Of(ReportFile report)
    {
        var columns = FieldsOf(report.Lines[0]);
        return [.. report.Lines.Skip(1).Select(FieldsOf).Select(fields => ByColumn(columns, fields))];
    }

    private static Dictionary<string, string> ByColumn(List<string> columns, List<string> fields)
    {
        Assert.Equal(columns.Count, fields.Count);
        return columns.Zip(fields).ToDictionary(pair => pair.First, pair => pair.Second);
    }

    /// <summary>A quoted field may hold separators and doubled quotes.</summary>
    private static List<string> FieldsOf(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var at = 0; at < line.Length; at++)
        {
            var character = line[at];
            if (quoted && character == Quote && at + 1 < line.Length && line[at + 1] == Quote)
            {
                field.Append(Quote);
                at++;
            }
            else if (character == Quote)
            {
                quoted = !quoted;
            }
            else if (character == Separator && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(character);
            }
        }
        fields.Add(field.ToString());
        return fields;
    }
}
