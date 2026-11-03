namespace AddedObjectRemover.Tests.Architecture;

/// <summary>A type (Member is null) or one member of a type, with the lines it covers. Type is the path "Outer.Inner".</summary>
internal sealed record CodeSpan(string Type, string? Member, int FirstLine, int LastLine)
{
    public bool Covers(int line) => FirstLine <= line && line <= LastLine;

    /// <summary>"Type.Member", or the member alone when it is outside every type.</summary>
    public string Place => Type == "" ? Member! : Member is null ? Type : $"{Type}.{Member}";
}

/// <summary>
/// Finds the types and members of a C# file from its tokens, so a line of code can be named by the member it is in,
/// "Type.Member". A line of a type outside every member (its header, a record's parameters, an enum's members) or
/// outside every type is in no member. Fields, properties, constructors and methods are members; a nested function or
/// lambda belongs to the member that holds it; overloads share one name. A member is named by its own name, never by a type
/// or tuple in its header; an operator by "operator" and its symbol.
/// </summary>
internal sealed class CodeOutline
{
    private const string OperatorKeyword = "operator";

    private static readonly string[] TypeKeywords = ["class", "struct", "interface", "record", "enum"];

    private readonly IReadOnlyList<CodeToken> tokens;
    private readonly string fileName;
    private readonly List<CodeSpan> spans = [];
    private int position;

    private CodeOutline(IReadOnlyList<CodeToken> tokens, string fileName)
    {
        this.tokens = tokens;
        this.fileName = fileName;
    }

    /// <summary>Every type and member found, in the order their reading ended (a type after its members).</summary>
    public IReadOnlyList<CodeSpan> Spans => spans;

    /// <param name="fileName">Names the file in the failure of a member the scan cannot name.</param>
    public static CodeOutline Of(string source, string fileName = "")
    {
        var outline = new CodeOutline(CodeTokens.Tokenize(source), fileName);
        outline.ReadScope("");
        return outline;
    }

    /// <summary>The one member that covers the line, or null when no member or more than one does.</summary>
    public CodeSpan? OnlyMemberOfLine(int line)
    {
        var members = spans.Where(span => span.Member is not null && span.Covers(line)).Take(2).ToList();
        return members.Count == 1 ? members[0] : null;
    }

    /// <summary>The member that holds the line; outside every member, the innermost type that does; outside both, "".</summary>
    public string PlaceOfLine(int line) =>
        OnlyMemberOfLine(line)?.Place
        ?? spans.Where(span => span.Member is null && span.Covers(line)).MaxBy(span => span.Type.Length)?.Type
        ?? "";

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
        var onlyAttributes = header.All(token => token.Text is "[" or "]");
        if (!onlyAttributes && name is null) throw new InvalidOperationException($"The scan cannot name the member that starts at line {firstLine} of {fileName}.");

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
            if (token.Word == OperatorKeyword) inOperator = true;
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
        var operatorAt = header.FindIndex(token => token.Word == OperatorKeyword);
        return operatorAt < 0 ? DeclaredNameOf(header) : OperatorNameOf(header, operatorAt);
    }

    /// <summary>The last name before the parameters at the outer level: a name or tuple inside the angle brackets of a type is not the member.</summary>
    private static string? DeclaredNameOf(List<CodeToken> header)
    {
        string? name = null;
        var angleDepth = 0;
        foreach (var token in header)
        {
            if (token.Text == "(" && name is not null && angleDepth == 0) break;
            if (token.Text == "<") angleDepth++;
            else if (token.Text == ">") angleDepth = Math.Max(0, angleDepth - 1);
            else if (angleDepth == 0 && token.Text is CodeTokens.Name or "this") name = token.Word;
        }
        return name;
    }

    /// <summary>"operator" and the symbol or target type written after it, so each operator of a type is its own member.</summary>
    private static string OperatorNameOf(List<CodeToken> header, int operatorAt)
    {
        var symbol = header.Skip(operatorAt + 1).TakeWhile(token => token.Text != "(").Select(WrittenText);
        return $"{OperatorKeyword} {string.Concat(symbol)}";
    }

    private static string WrittenText(CodeToken token) => token.Word.Length > 0 ? token.Word : token.Text;

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
