using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// One production source file, located by its path inside its project.
/// </summary>
/// <param name="Path">With '/' separators; relative to the entry project folder for its files, otherwise to the solution folder (such as "Tools/BoxAndTransformMath/Box.cs").</param>
internal sealed partial record SourceFile(string Project, string Path, string FullPath)
{
    private static readonly Lazy<IReadOnlyList<SourceFile>> Files = new(Read);

    /// <summary>The code with comments removed and string and character literals emptied, so text in them never counts.</summary>
    public string Code { get; } = CodeOf(File.ReadAllText(FullPath));

    /// <summary>Like <see cref="Code"/>, but the expressions inside interpolated strings are kept, because the names they use count as uses.</summary>
    public string CodeWithInterpolatedExpressions { get; } = CodeWithExpressionsOf(File.ReadAllText(FullPath));

    /// <summary>The text of every string literal as written between its quotes (interpolation holes included), comments excluded.</summary>
    public IReadOnlyList<string> StringLiterals { get; } = StringLiteralsOf(File.ReadAllText(FullPath));

    /// <summary>The string literals of <see cref="StringLiterals"/> with the line each starts on.</summary>
    public IReadOnlyList<(string Literal, int Line)> StringLiteralLines { get; } = StringLiteralLinesOf(File.ReadAllText(FullPath));

    public string Description => $"{Project}: {Path}";

    /// <summary>The file is one of the listed files, inside one of the listed folders, or in one of the listed projects.</summary>
    public bool IsIn(IEnumerable<string> places) =>
        places.Any(place => Project == place || Path == place || Path.StartsWith(place + "/", StringComparison.Ordinal));

    public static IReadOnlyList<SourceFile> All() => Files.Value;

    public static IEnumerable<string> DeclaredTypes(string code) =>
        TypeDeclarationPattern().Matches(code).Select(match => match.Groups["name"].Value);

    public static string CodeOf(string text) =>
        CommentsAndLiteralsPattern().Replace(text, match => match.Value.StartsWith('/') ? "" : "\"\"");

    public static IReadOnlyList<string> StringLiteralsOf(string text) => [.. StringLiteralLinesOf(text).Select(literal => literal.Literal)];

    /// <summary>Each string literal as written between its quotes, with the line it starts on.</summary>
    public static IReadOnlyList<(string Literal, int Line)> StringLiteralLinesOf(string text)
    {
        var literals = new List<(string Literal, int Line)>();
        var line = 1;
        var counted = 0;
        foreach (Match match in CommentsAndLiteralsPattern().Matches(text))
        {
            line += text.AsSpan(counted, match.Index - counted).Count('\n');
            counted = match.Index;
            if (match.Value[0] is '"' or '@' or '$') literals.Add((match.Value.TrimStart('$', '@')[1..^1], line));
        }
        return literals;
    }

    /// <summary>Like <see cref="CodeOf"/>, but the line breaks of a removed comment or an emptied literal stay, so a line of the code is the same line of the source.</summary>
    public static string CodeKeepingLinesOf(string text) =>
        CommentsAndLiteralsPattern().Replace(text, match => (match.Value.StartsWith('/') ? "" : "\"\"") + new string('\n', match.Value.Count(character => character == '\n')));

    public static string CodeWithExpressionsOf(string text) =>
        CommentsAndLiteralsPattern().Replace(text, match => match.Value[0] switch
        {
            '/' => "",
            '$' => $" {string.Join(" ; ", InterpolationPattern().Matches(match.Value).Select(hole => hole.Groups["expression"].Value))} ",
            _ => "\"\"",
        });

    private static IReadOnlyList<SourceFile> Read() =>
        ProductionSources.Projects()
            .SelectMany(project => project.Files.Select(file => new SourceFile(
                project.Project, System.IO.Path.GetRelativePath(PathBase(project), file).Replace('\\', '/'), file)))
            .ToList();

    public static string PathBase(ProjectSources project) =>
        project.Folder == SolutionLayout.ProductionFolder() ? project.Folder : SolutionLayout.Root();

    /// <summary>Top-level types only (declared at the start of a line, in a file-scoped namespace); a nested type is reached through its outer type.</summary>
    [GeneratedRegex(@"^(?:\w+\s+)*?(?:class|struct|interface|enum|record)\s+(?:(?:class|struct)\s+)?(?<name>[A-Z]\w*)", RegexOptions.Multiline)]
    private static partial Regex TypeDeclarationPattern();

    /// <summary>A hole of an interpolated string (not an escaped brace); only as far as the first quote inside it.</summary>
    [GeneratedRegex(@"(?<!\{)\{(?<expression>[^{}]+)\}")]
    private static partial Regex InterpolationPattern();

    [GeneratedRegex("""//[^\n]*|(?s:/\*.*?\*/)|@"(?:[^"]|"")*"|\$?"(?:\\.|[^"\\\n])*"|'(?:\\.|[^'\\\n])'""")]
    private static partial Regex CommentsAndLiteralsPattern();
}
