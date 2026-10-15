namespace AddedObjectRemover.Tests.Architecture;

/// <summary>A type (Member is null) or one member of a type, with the lines it covers. Type is the path "Outer.Inner".</summary>
internal sealed record CodeSpan(string Type, string? Member, int FirstLine, int LastLine)
{
    public bool Covers(int line) => FirstLine <= line && line <= LastLine;

    /// <summary>"Type.Member", or the member alone when it is outside every type.</summary>
    public string Place => Type == "" ? Member! : Member is null ? Type : $"{Type}.{Member}";
}

/// <summary>
/// Finds the types and members of a C# file from its tokens, so a line of code can be named by where it is:
/// "Type.Member", or just "Type" for a line of a type outside every member (its header, a record's parameters).
/// A line outside every type has no place. Fields, properties, constructors and methods are members; a nested
/// function or lambda belongs to the member that holds it; overloads share one name.
/// </summary>
internal sealed class CodeOutline
{
    private static readonly string[] TypeKeywords = ["class", "struct", "interface", "record", "enum"];

    private readonly IReadOnlyList<CodeToken> tokens;
    private readonly List<CodeSpan> spans = [];
    private int position;

    private CodeOutline(IReadOnlyList<CodeToken> tokens)
    {
        this.tokens = tokens;
    }

    public static CodeOutline Of(string source)
    {
        var outline = new CodeOutline(CodeTokens.Tokenize(source));
        outline.ReadScope("");
        return outline;
    }

    /// <summary>The members that cover the line; when none does, the innermost type that does; when none does, nothing.</summary>
    public IReadOnlyList<string> PlacesOfLine(int line)
    {
        var members = spans.Where(span => span.Member is not null && span.Covers(line)).Select(span => span.Place).ToList();
        if (members.Count > 0) return members;
        var type = spans.Where(span => span.Covers(line)).MaxBy(span => span.Type.Length);
        return type is null ? [] : [type.Place];
    }

    private void ReadScope(string typePath)
    {
        while (position < tokens.Count && tokens[position].Text != "}") ReadDeclaration(typePath);
        position = Math.Min(position + 1, tokens.Count);
    }

    private void ReadDeclaration(string typePath)
    {
        var firstLine = tokens[position].Line;
        var header = ReadHeader();
        var firstWord = header.Select(token => token.Word).FirstOrDefault(word => !string.IsNullOrEmpty(word));

        if (firstWord == "using") SkipStatement();
        else if (firstWord == "namespace") ReadNamespace(typePath);
        else if (TypeOf(header) is { } type) ReadType(typePath, type, firstLine);
        else ReadMember(typePath, header, firstLine);
    }

    private void ReadNamespace(string typePath)
    {
        if (CurrentText() == "{")
        {
            position++;
            ReadScope(typePath);
        }
        else position++;
    }

    private void ReadType(string typePath, (string Keyword, string Name) type, int firstLine)
    {
        var path = typePath == "" ? type.Name : $"{typePath}.{type.Name}";
        if (CurrentText() != "{") position = Math.Min(position + 1, tokens.Count);
        else if (type.Keyword == "enum") SkipBlock();
        else
        {
            position++;
            ReadScope(path);
        }
        spans.Add(new CodeSpan(path, null, firstLine, tokens[position - 1].Line));
    }

    private void ReadMember(string typePath, List<CodeToken> header, int firstLine)
    {
        var name = MemberNameOf(header);
        if (header.Count > 0 && name is null) throw new InvalidOperationException($"The scan cannot name the member that starts at line {firstLine}.");

        switch (CurrentText())
        {
            case "{":
                SkipBlock();
                if (CurrentText() == "=") SkipStatement();
                break;
            case "=":
                SkipStatement();
                break;
            case ";":
                position++;
                break;
        }
        if (name is not null) spans.Add(new CodeSpan(typePath, name, firstLine, tokens[position - 1].Line));
    }

    /// <summary>The tokens up to the first brace, semicolon or initializer, with the inside of brackets left out.</summary>
    private List<CodeToken> ReadHeader()
    {
        var header = new List<CodeToken>();
        var depth = 0;
        var inOperator = false;
        while (position < tokens.Count)
        {
            var token = tokens[position];
            if (token.Word == "operator") inOperator = true;
            if (depth == 0 && (token.Text is "{" or ";" or "}" || (token.Text == "=" && !inOperator))) break;
            if (depth == 0 && token.Text == "(") inOperator = false;
            if (depth == 0) header.Add(token);
            depth += DepthChange(token.Text);
            position++;
        }
        return header;
    }

    private static (string Keyword, string Name)? TypeOf(List<CodeToken> header)
    {
        var limit = header.FindIndex(token => token.Text == "(");
        if (limit < 0) limit = header.Count;
        for (var index = 0; index < limit; index++)
        {
            var isConstraint = index > 0 && header[index - 1].Text is ":" or ",";
            if (!TypeKeywords.Contains(header[index].Text) || isConstraint) continue;
            var name = header.FindIndex(index + 1, token => token.Text == CodeTokens.Name);
            return name < 0 ? null : (header[index].Text, header[name].Word);
        }
        return null;
    }

    private static string? MemberNameOf(List<CodeToken> header)
    {
        string? name = null;
        var angleDepth = 0;
        foreach (var token in header)
        {
            if (token.Text == "(" && name is not null) break;
            if (token.Text == "<") angleDepth++;
            else if (token.Text == ">") angleDepth = Math.Max(0, angleDepth - 1);
            else if (token.Word == "operator") name = token.Word;
            else if (angleDepth == 0 && token.Text is CodeTokens.Name or "this") name = token.Word;
        }
        return name;
    }

    private string CurrentText() => position < tokens.Count ? tokens[position].Text : "";

    private void SkipBlock()
    {
        var depth = 0;
        do
        {
            depth += DepthChange(tokens[position].Text);
            position++;
        }
        while (depth > 0 && position < tokens.Count);
    }

    private void SkipStatement()
    {
        var depth = 0;
        while (position < tokens.Count)
        {
            var text = tokens[position++].Text;
            depth += DepthChange(text);
            if (text == ";" && depth == 0) return;
        }
    }

    private static int DepthChange(string text) => text switch
    {
        "(" or "[" or "{" => 1,
        ")" or "]" or "}" => -1,
        _ => 0,
    };
}
