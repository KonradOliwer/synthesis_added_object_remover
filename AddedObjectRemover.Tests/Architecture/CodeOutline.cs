using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>A type (Member is null) or one member of a type, with the lines it covers. Type is the path "Outer.Inner".</summary>
internal sealed record CodeSpan(string Type, string? Member, int FirstLine, int LastLine)
{
    public bool Covers(int line) => FirstLine <= line && line <= LastLine;

    /// <summary>"Type.Member", or the type alone for a type.</summary>
    public string Place => Member is null ? Type : $"{Type}.{Member}";
}

/// <summary>
/// One member of a type: its type path, its name, the node that holds its value (the variable of a field, otherwise the
/// declaration) and its declaration.
/// </summary>
internal sealed record OutlinedMember(string Type, string Name, SyntaxNode Node, MemberDeclarationSyntax Declaration);

/// <summary>
/// The types and members of a C# file, so a line of code can be named by the member it is in, "Type.Member". A line of a
/// type outside every member (its header, a record's parameters, an enum's members) or outside every type is in no member.
/// Fields (each variable on its own), events, properties, indexers ("this"), constructors, destructors, operators
/// ("operator +", "operator int"), methods and nested delegates are members; a nested function or lambda belongs to the member
/// that holds it; overloads share one name. A member's lines start at its attributes.
/// </summary>
internal sealed class CodeOutline
{
    public const string IndexerName = "this";
    private const string OperatorPrefix = "operator ";

    /// <summary>Every type and member, in the order their declarations end (a type after its members).</summary>
    private readonly IReadOnlyList<CodeSpan> spans;

    private CodeOutline(IReadOnlyList<CodeSpan> spans) => this.spans = spans;

    public static CodeOutline Of(SyntaxNode root) => new([.. SpansOrderedByEnd(root)]);

    /// <summary>The member name of the operator written with the symbol, such as "operator +".</summary>
    public static string OperatorName(string symbol) => OperatorPrefix + symbol;

    /// <summary>Every member of every type, in the order they are written.</summary>
    public static IEnumerable<OutlinedMember> MembersOf(SyntaxNode root) =>
        root.DescendantNodes().OfType<MemberDeclarationSyntax>()
            .Where(declaration => declaration.Parent is TypeDeclarationSyntax)
            .SelectMany(declaration => NamedNodesOf(declaration).Select(named => new OutlinedMember(PathOf(declaration), named.Name, named.Node, declaration)));

    /// <summary>The one member that covers the line, or null when no member or more than one does.</summary>
    public CodeSpan? OnlyMemberOfLine(int line)
    {
        var members = spans.Where(span => span.Member is not null && span.Covers(line)).ToList();
        return members.Count == 1 ? members[0] : null;
    }

    /// <summary>The member that holds the line; outside every member, the innermost type that does; outside both, "".</summary>
    public string PlaceOfLine(int line) =>
        OnlyMemberOfLine(line)?.Place
        ?? spans.Where(span => span.Member is null && span.Covers(line)).MaxBy(span => span.Type.Length)?.Type
        ?? "";

    private static IEnumerable<CodeSpan> SpansOrderedByEnd(SyntaxNode root)
    {
        var types = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Select(type => (Node: (SyntaxNode)type, Span: SpanOf(PathOf(type), null, type)));
        var members = MembersOf(root).Select(member => (Node: (SyntaxNode)member.Declaration, Span: SpanOf(member.Type, member.Name, member.Declaration)));
        return types.Concat(members).OrderBy(entry => entry.Node.Span.End).ThenBy(entry => entry.Node.Span.Start).Select(entry => entry.Span);
    }

    private static CodeSpan SpanOf(string type, string? member, SyntaxNode node) =>
        new(type, member, SyntaxLines.FirstLineOf(node), SyntaxLines.LastLineOf(node));

    /// <summary>The path "Outer.Inner" of the types that hold the node, the node itself included when it is a type.</summary>
    private static string PathOf(SyntaxNode node) =>
        string.Join(".", node.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().Reverse().Select(type => type.Identifier.Text));

    private static IEnumerable<(string Name, SyntaxNode Node)> NamedNodesOf(MemberDeclarationSyntax declaration) => declaration switch
    {
        BaseFieldDeclarationSyntax field => field.Declaration.Variables.Select(variable => (variable.Identifier.Text, (SyntaxNode)variable)),
        PropertyDeclarationSyntax property => [(property.Identifier.Text, property)],
        EventDeclarationSyntax @event => [(@event.Identifier.Text, @event)],
        IndexerDeclarationSyntax indexer => [(IndexerName, indexer)],
        MethodDeclarationSyntax method => [(method.Identifier.Text, method)],
        ConstructorDeclarationSyntax constructor => [(constructor.Identifier.Text, constructor)],
        DestructorDeclarationSyntax destructor => [(destructor.Identifier.Text, destructor)],
        OperatorDeclarationSyntax @operator => [(OperatorName(@operator.OperatorToken.Text), @operator)],
        ConversionOperatorDeclarationSyntax conversion => [(OperatorName(conversion.Type.ToString()), conversion)],
        DelegateDeclarationSyntax @delegate => [(@delegate.Identifier.Text, @delegate)],
        _ => [],
    };
}
