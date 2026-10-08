using System.Text;

namespace AddedObjectRemover.Tests.Architecture;

internal readonly record struct CodeToken(string Text, int Line);

/// <summary>
/// Reads C# source into tokens for the copied-code scan. Comments are dropped, every string or character
/// literal is one token, names and numbers are replaced by placeholders so a copy with renamed variables still
/// matches. Lines that hold only braces, a using or an attribute are dropped, because they repeat everywhere.
/// </summary>
internal static class CodeTokens
{
    public const string Name = "name";
    public const string Number = "number";
    public const string Text = "text";

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false",
        "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "record", "ref", "return", "sbyte", "sealed", "short", "sizeof", "static", "string", "struct", "switch", "this",
        "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "var", "virtual", "void",
        "volatile", "while", "yield", "get", "set", "init", "when", "where", "select", "from", "orderby", "let", "partial",
    };

    private const int MinimumRawQuotes = 3;
    private static readonly string[] LineOnlyPunctuation = ["{", "}", ";", ","];

    /// <summary>The tokens that count for the scan: comments and lines of braces, usings and attributes removed.</summary>
    public static IReadOnlyList<CodeToken> Significant(string source)
    {
        var significant = new List<CodeToken>();
        foreach (var line in Tokenize(source).GroupBy(token => token.Line).OrderBy(group => group.Key))
        {
            var tokens = line.ToList();
            if (!IsSkippedLine(tokens)) significant.AddRange(tokens);
        }
        return significant;
    }

    public static IReadOnlyList<CodeToken> Tokenize(string source)
    {
        var tokens = new List<CodeToken>();
        var position = 0;
        var line = 1;
        while (position < source.Length)
        {
            var current = source[position];
            if (current == '\n') { line++; position++; }
            else if (char.IsWhiteSpace(current)) position++;
            else if (current == '#' && AtLineStart(source, position)) position = EndOfLine(source, position);
            else if (StartsWith(source, position, "//")) position = EndOfLine(source, position);
            else if (StartsWith(source, position, "/*")) position = SkipBlockComment(source, position, ref line);
            else if (FindStringStart(source, position) is { } start)
            {
                tokens.Add(new CodeToken(Text, line));
                position = SkipString(source, start, ref line);
            }
            else if (current == '\'')
            {
                tokens.Add(new CodeToken(Text, line));
                position = SkipCharacter(source, position);
            }
            else if (char.IsLetter(current) || current == '_' || current == '@')
            {
                var end = EndOfWord(source, position + 1);
                var word = source[position..end];
                tokens.Add(new CodeToken(Keywords.Contains(word) ? word : Name, line));
                position = end;
            }
            else if (char.IsDigit(current))
            {
                tokens.Add(new CodeToken(Number, line));
                position = EndOfNumber(source, position + 1);
            }
            else
            {
                tokens.Add(new CodeToken(current.ToString(), line));
                position++;
            }
        }
        return tokens;
    }

    private static bool IsSkippedLine(List<CodeToken> tokens)
    {
        if (tokens.All(token => LineOnlyPunctuation.Contains(token.Text))) return true;
        if (tokens[0].Text == "[" && tokens[^1].Text == "]") return true;
        return tokens[0].Text == "using" && tokens.Count > 1 && tokens[1].Text is not "var" and not "(";
    }

    private static bool AtLineStart(string source, int position)
    {
        for (var index = position - 1; index >= 0 && source[index] != '\n'; index--)
        {
            if (!char.IsWhiteSpace(source[index])) return false;
        }
        return true;
    }

    private static bool StartsWith(string source, int position, string text) =>
        string.CompareOrdinal(source, position, text, 0, text.Length) == 0;

    private static int EndOfLine(string source, int position)
    {
        var end = source.IndexOf('\n', position);
        return end < 0 ? source.Length : end;
    }

    private static int SkipBlockComment(string source, int position, ref int line)
    {
        var end = source.IndexOf("*/", position + 2, StringComparison.Ordinal);
        var stop = end < 0 ? source.Length : end + 2;
        line += source.AsSpan(position, stop - position).Count('\n');
        return stop;
    }

    private static int EndOfWord(string source, int position)
    {
        while (position < source.Length && (char.IsLetterOrDigit(source[position]) || source[position] == '_')) position++;
        return position;
    }

    private static int EndOfNumber(string source, int position)
    {
        while (position < source.Length && (char.IsLetterOrDigit(source[position]) || source[position] == '_' || IsDecimalPoint(source, position))) position++;
        return position;
    }

    private static bool IsDecimalPoint(string source, int position) =>
        source[position] == '.' && position + 1 < source.Length && char.IsDigit(source[position + 1]);

    private static int SkipCharacter(string source, int position)
    {
        position++;
        while (position < source.Length && source[position] != '\'')
        {
            position += source[position] == '\\' ? 2 : 1;
        }
        return position + 1;
    }

    private readonly record struct StringStart(int QuotePosition, bool Verbatim, bool Interpolated);

    private static StringStart? FindStringStart(string source, int position)
    {
        var index = position;
        var verbatim = false;
        var interpolated = false;
        while (index < source.Length && source[index] is '$' or '@')
        {
            if (source[index] == '@') verbatim = true; else interpolated = true;
            index++;
        }
        return index < source.Length && source[index] == '"' ? new StringStart(index, verbatim, interpolated) : null;
    }

    private static int SkipString(string source, StringStart start, ref int line)
    {
        var quotes = CountRun(source, start.QuotePosition, '"');
        var position = start.QuotePosition + quotes;
        if (quotes >= MinimumRawQuotes)
        {
            var close = source.IndexOf(new string('"', quotes), position, StringComparison.Ordinal);
            var stop = (close < 0 ? source.Length : close) + quotes;
            line += source.AsSpan(position, Math.Min(stop, source.Length) - position).Count('\n');
            return stop;
        }
        return start.Verbatim ? SkipVerbatim(source, position, start.Interpolated, ref line) : SkipRegular(source, position, start.Interpolated, ref line);
    }

    private static int CountRun(string source, int position, char character)
    {
        var count = 0;
        while (position + count < source.Length && source[position + count] == character) count++;
        return count;
    }

    private static int SkipRegular(string source, int position, bool interpolated, ref int line)
    {
        while (position < source.Length && source[position] != '"')
        {
            if (source[position] == '\\') position += 2;
            else if (interpolated && source[position] == '{') position = SkipHole(source, position, ref line);
            else position++;
        }
        return position + 1;
    }

    private static int SkipVerbatim(string source, int position, bool interpolated, ref int line)
    {
        while (position < source.Length)
        {
            var current = source[position];
            if (current == '"' && StartsWith(source, position, "\"\"")) position += 2;
            else if (current == '"') return position + 1;
            else if (interpolated && current == '{') position = SkipHole(source, position, ref line);
            else
            {
                if (current == '\n') line++;
                position++;
            }
        }
        return position;
    }

    /// <summary>Skips an interpolation hole, or an escaped "{{", with any string inside the hole.</summary>
    private static int SkipHole(string source, int position, ref int line)
    {
        if (StartsWith(source, position, "{{")) return position + 2;
        var depth = 0;
        do
        {
            var current = source[position];
            if (current == '{') depth++;
            else if (current == '}') depth--;
            if (current == '"' && FindStringStart(source, position) is { } nested) position = SkipString(source, nested, ref line);
            else position++;
        }
        while (depth > 0 && position < source.Length);
        return position;
    }
}
