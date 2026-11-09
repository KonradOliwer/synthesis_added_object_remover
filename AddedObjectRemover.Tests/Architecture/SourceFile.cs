using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// One production source file, located by its path inside its project, with its syntax tree. The queries read tokens and
/// syntax nodes, so names in comments and literals never count. A receiver is matched as written alone or as the last part of a
/// qualified name (<c>Console</c> in <c>System.Console</c>).
/// </summary>
/// <param name="Path">With '/' separators; relative to the entry project folder for its files, otherwise to the solution folder (such as "Tools/BoxAndTransformMath/Box.cs").</param>
internal sealed record SourceFile(string Project, string Path, string FullPath, CompilationUnitSyntax Root)
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static readonly Lazy<IReadOnlyList<SourceFile>> Files = new(ReadProductionFiles);

    /// <summary>The text of every string literal and interpolated string: see <see cref="TextLiterals.TextOf"/>.</summary>
    public IReadOnlyList<string> StringLiterals => field ??= [.. TextLiterals.StringLiteralsIn(Root).Select(TextLiterals.TextOf)];

    /// <summary>See <see cref="TextLiterals.WordingLiteralLinesIn"/>.</summary>
    public IReadOnlyList<(string Literal, int Line)> WordingLiteralLines => field ??= TextLiterals.WordingLiteralLinesIn(Root);

    /// <summary>Top-level types and delegates only; a nested type is reached through its outer type.</summary>
    public IEnumerable<string> DeclaredTypes =>
        Root.DescendantNodes(node => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
            .Select(node => node switch
            {
                BaseTypeDeclarationSyntax type => type.Identifier.Text,
                DelegateDeclarationSyntax @delegate => @delegate.Identifier.Text,
                _ => null,
            })
            .OfType<string>();

    public CodeOutline Outline => field ??= CodeOutline.Of(Root);

    public IEnumerable<string> Identifiers => SyntaxNames.IdentifierTokensIn(Root).Select(token => token.Text);

    public string Description => $"{Project}: {Path}";

    /// <summary>The file is one of the listed files, inside one of the listed folders, or in one of the listed projects.</summary>
    public bool IsIn(IEnumerable<string> places) =>
        places.Any(place => Project == place || Path == place || Path.StartsWith(place + "/", StringComparison.Ordinal));

    public bool MentionsIdentifier(string identifier) => Identifiers.Contains(identifier);

    /// <summary>Each member access <c>x.member</c> and member binding <c>x?.member</c>.</summary>
    public IEnumerable<ExpressionSyntax> MemberReads(string member) =>
        Root.DescendantNodes().OfType<ExpressionSyntax>().Where(node => node switch
        {
            MemberAccessExpressionSyntax access => access.Name.Identifier.Text == member,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text == member,
            _ => false,
        });

    public bool Reads(string member) => MemberReads(member).Any();

    /// <summary>A read of the member other than <c>excludedReceiver.member</c> on the name itself.</summary>
    public bool ReadsExceptOn(string member, string excludedReceiver) =>
        MemberReads(member).Any(read => read is not MemberAccessExpressionSyntax access || !SyntaxNames.IsIdentifier(access.Expression, excludedReceiver));

    /// <summary>Each member access <c>receiver.x</c> on the name <paramref name="receiver"/>.</summary>
    public IEnumerable<MemberAccessExpressionSyntax> MemberAccessesOn(string receiver) =>
        Root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Where(access => SyntaxNames.EndsWithName(access.Expression, receiver));

    public bool Reads(string receiver, string member) => MemberAccessesOn(receiver).Any(access => access.Name.Identifier.Text == member);

    public bool ReadsOn(string receiver) => MemberAccessesOn(receiver).Any();

    public IEnumerable<InvocationExpressionSyntax> CallsOf(params string[] methods) => SyntaxNames.CallsIn(Root, methods);

    public bool Calls(string method) => CallsOf(method).Any();

    public bool Creates(string typeName) =>
        Root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Any(creation => SyntaxNames.RightmostName(creation.Type) == typeName);

    /// <summary>A using directive of any kind (static, alias, global, inside a namespace) of a namespace that is or lies below one of the prefixes.</summary>
    public bool Uses(params string[] namespacePrefixes) =>
        Root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Select(directive => directive.NamespaceOrType.ToString())
            .Any(name => namespacePrefixes.Any(prefix => name == prefix || name.StartsWith(prefix + ".", StringComparison.Ordinal)));

    /// <summary>The type name followed by a declared name, also as a nullable: a field, local, parameter, property or method return typed so.</summary>
    public bool DeclaresWithType(string typeName) =>
        SyntaxNames.IdentifierTokensIn(Root).Any(token => token.Text == typeName && TokenAfterNullableMark(token).IsKind(SyntaxKind.IdentifierToken));

    public static IReadOnlyList<SourceFile> All() => Files.Value;

    public static CompilationUnitSyntax Parse(string text) => (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(text, ParseOptions).GetRoot();

    /// <summary>A piece of code read as a file of no project.</summary>
    public static SourceFile OfSnippet(string text) => new("", "", "", Parse(text));

    public static string PathBase(ProjectSources project) =>
        project.Folder == SolutionLayout.ProductionFolder() ? project.Folder : SolutionLayout.Root();

    private static IReadOnlyList<SourceFile> ReadProductionFiles() =>
        ProductionSources.Projects()
            .SelectMany(project => project.Files.Select(file => new SourceFile(
                project.Project, System.IO.Path.GetRelativePath(PathBase(project), file).Replace('\\', '/'), file, Parse(File.ReadAllText(file)))))
            .ToList();

    private static SyntaxToken TokenAfterNullableMark(SyntaxToken token) =>
        token.GetNextToken() is var next && next.IsKind(SyntaxKind.QuestionToken) ? next.GetNextToken() : next;
}
