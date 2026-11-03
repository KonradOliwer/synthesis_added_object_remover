namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// One member of a type as its source file writes it: a field, property, constructor or method, each overload on its own.
/// Type is the path "Outer.Inner". Tokens are those of the lines the member covers, so a neighbour on the same line is included.
/// </summary>
internal sealed record WrittenMember(string SourcePath, string Type, string Name, int FirstLine, int LastLine, IReadOnlyList<CodeToken> Tokens)
{
    private const string ConstKeyword = "const";
    private const string DecidedTypeName = "Decided";
    private const string OpenCall = "(";
    private const string CloseCall = ")";
    private static readonly string[] AccessorKeywords = ["get", "set", "init"];
    private static readonly string[] NotAnAssignmentBefore = ["!", "<", ">", "=", "operator"];
    private static readonly string[] NotAnAssignmentAfter = ["=", ">"];

    /// <summary>
    /// The tokens by which a value can decide or compute something: a comparison, a further "=" (also of a lambda), a logical
    /// or pattern operator, a conditional or null test, a switch, a throw, or arithmetic other than a sum ("-" also of a
    /// negative number). "&lt;" and "&gt;" also stand for type arguments; both only make the check stricter.
    /// </summary>
    private static readonly HashSet<string> DecidingTokens = ["<", ">", "=", "!", "&", "|", "?", "is", "switch", "throw", "*", "/", "%", "-", "^"];

    /// <summary>"path:first-last", for a message that names where the member is.</summary>
    public string Where => $"{SourcePath}:{FirstLine}-{LastLine}";

    public string Place => $"{Type}.{Name}";

    public int LineCount => LastLine - FirstLine + 1;

    public bool IsConstructor => Name == Type[(Type.LastIndexOf('.') + 1)..];

    /// <summary>The member is written with <c>=&gt;</c> before its body.</summary>
    public bool IsExpressionBodied => FirstEqualsOutsideBrackets() is var index && index >= 0 && TextAt(index + 1) == ">";

    /// <summary>A field or property with a value written after it (<c>= value</c>); a constant has none.</summary>
    public bool HasInitialiser =>
        Tokens.All(token => token.Text != ConstKeyword)
            && FirstEqualsOutsideBrackets() is var index && index >= 0
            && !NotAnAssignmentAfter.Contains(TextAt(index + 1))
            && !NotAnAssignmentBefore.Contains(TextAt(index - 1));

    /// <summary>An expression body or an initialiser, and no other statement: the member ends with its one semicolon.</summary>
    public bool IsOneExpression => (IsExpressionBodied || HasInitialiser) && StatementEndCount() == 1;

    /// <summary>
    /// One expression that only builds or reads a value: a fixed value, a factory over its arguments, a plain accessor or
    /// indexer, a sum. Its value holds no <see cref="DecidingTokens"/> and calls nothing but a constructor
    /// (<c>new(</c>, <c>new Type(</c>); a constructor itself always decides how its object is built.
    /// </summary>
    public bool DecidesNothing => !IsConstructor && IsOneExpression && ValueTokens() is var value && !value.Any(IsDecidingToken) && !HoldsCall(value);

    /// <summary>The member's header, up to its first parenthesis, names the type <c>Decided</c>: a step entry returns the decisions it added to.</summary>
    public bool ReturnsDecided => Tokens.TakeWhile(token => token.Text != OpenCall).Any(token => token.Word == DecidedTypeName);

    /// <summary>The token index of each call of a method with one of the names, written <c>receiver.Name(</c>.</summary>
    public IReadOnlyList<int> IndicesOfCallsTo(IReadOnlyCollection<string> methodNames)
    {
        var indices = new List<int>();
        for (var index = 1; index + 1 < Tokens.Count; index++)
        {
            if (Tokens[index - 1].Text == "." && methodNames.Contains(Tokens[index].Word) && Tokens[index + 1].Text == OpenCall) indices.Add(index);
        }
        return indices;
    }

    /// <summary>The token range, first parenthesis to last, of each call written <c>Type.Method(</c> or <c>Type.Method&lt;T&gt;(</c>.</summary>
    public IReadOnlyList<(int Open, int Close)> ArgumentRangesOfCallsTo(string typeName, string methodName)
    {
        var ranges = new List<(int Open, int Close)>();
        for (var index = 0; index + 2 < Tokens.Count; index++)
        {
            if (Tokens[index].Word != typeName || Tokens[index + 1].Text != "." || Tokens[index + 2].Word != methodName) continue;
            var open = FirstOpenCallBeforeStatementEnd(index + 3);
            if (open >= 0) ranges.Add((open, CloseOfCallAt(open)));
        }
        return ranges;
    }

    public static IReadOnlyList<WrittenMember> Of(IEnumerable<SourceFile> files) =>
        [.. files.SelectMany(file => Of(file.Path, File.ReadAllText(file.FullPath)))];

    public static IReadOnlyList<WrittenMember> Of(string sourcePath, string text)
    {
        var tokens = CodeTokens.Tokenize(text);
        return
        [
            .. CodeOutline.Of(text, sourcePath).Spans
                .Where(span => span.Member is not null)
                .Select(span => new WrittenMember(sourcePath, span.Type, span.Member!, span.FirstLine, span.LastLine, [.. tokens.Where(token => span.Covers(token.Line))])),
        ];
    }

    /// <summary>The index of the first <c>=</c> outside every bracket: the start of an initialiser or of a <c>=&gt;</c>; -1 when there is none.</summary>
    private int FirstEqualsOutsideBrackets()
    {
        var depth = 0;
        for (var index = 0; index < Tokens.Count; index++)
        {
            var text = Tokens[index].Text;
            if (text == "=" && depth == 0) return index;
            depth += text switch
            {
                "(" or "[" or "{" => 1,
                ")" or "]" or "}" => -1,
                _ => 0,
            };
        }
        return -1;
    }

    /// <summary>The semicolons that end a statement or the member; the one after <c>get</c>, <c>set</c> or <c>init</c> ends an abstract accessor.</summary>
    private int StatementEndCount() =>
        Tokens.Where((token, index) => token.Text == ";" && !(index > 0 && AccessorKeywords.Contains(Tokens[index - 1].Text))).Count();

    private string TextAt(int index) => index >= 0 && index < Tokens.Count ? Tokens[index].Text : "";

    /// <summary>The tokens after the <c>=</c> or <c>=&gt;</c> that starts the value.</summary>
    private List<CodeToken> ValueTokens()
    {
        var start = FirstEqualsOutsideBrackets() + (IsExpressionBodied ? 2 : 1);
        return [.. Tokens.Skip(start)];
    }

    private static bool IsDecidingToken(CodeToken token) => DecidingTokens.Contains(token.Text);

    /// <summary>A "(" right after a name or keyword other than the "new" of a constructor or the type name it creates.</summary>
    private static bool HoldsCall(List<CodeToken> value) =>
        Enumerable.Range(1, Math.Max(0, value.Count - 1)).Any(index =>
            value[index].Text == OpenCall && value[index - 1].Word != "" && value[index - 1].Text != "new" && !(index > 1 && value[index - 2].Text == "new"));

    private int FirstOpenCallBeforeStatementEnd(int start)
    {
        for (var index = start; index < Tokens.Count && Tokens[index].Text != ";"; index++)
        {
            if (Tokens[index].Text == OpenCall) return index;
        }
        return -1;
    }

    private int CloseOfCallAt(int open)
    {
        var depth = 0;
        for (var index = open; index < Tokens.Count; index++)
        {
            depth += Tokens[index].Text == OpenCall ? 1 : Tokens[index].Text == CloseCall ? -1 : 0;
            if (depth == 0) return index;
        }
        return Tokens.Count - 1;
    }
}
