using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// One member of a type as its source file writes it: a field (each variable on its own), property, constructor or method,
/// each overload on its own. Type is the path "Outer.Inner". Node holds the member's value: the variable of a field, otherwise
/// the declaration.
/// </summary>
internal sealed record WrittenMember(string SourcePath, string Type, string Name, int FirstLine, int LastLine, SyntaxNode Node)
{
    /// <summary>"path:first-last", for a message that names where the member is.</summary>
    public string Location => $"{SourcePath}:{FirstLine}-{LastLine}";

    public string Place => $"{Type}.{Name}";

    public bool IsConstructor => Node is ConstructorDeclarationSyntax;

    /// <summary>A field or property with a value written after it (<c>= value</c>); a constant has none.</summary>
    public bool HasInitialiser => Initialiser is not null;

    /// <summary>An expression body or an initialiser that holds no statement.</summary>
    public bool IsOneExpression => Value is { } value && !value.DescendantNodes().OfType<StatementSyntax>().Any();

    /// <summary>
    /// One expression that only builds or reads a value: a fixed value, a factory over its arguments, a plain accessor or
    /// indexer, a sum. Its value compares, tests, branches, switches, throws, computes other than by a sum, assigns, holds no
    /// lambda and calls nothing but a constructor; a constructor itself always decides how its object is built.
    /// </summary>
    public bool DecidesNothing => !IsConstructor && IsOneExpression && !Value!.DescendantNodesAndSelf().Any(IsDeciding);

    /// <summary>The member's own <c>=&gt;</c> body; an accessor's <c>get =&gt;</c> is not one.</summary>
    private ArrowExpressionClauseSyntax? ExpressionBody => Node switch
    {
        BaseMethodDeclarationSyntax method => method.ExpressionBody,
        PropertyDeclarationSyntax property => property.ExpressionBody,
        IndexerDeclarationSyntax indexer => indexer.ExpressionBody,
        _ => null,
    };

    private EqualsValueClauseSyntax? Initialiser => Node switch
    {
        VariableDeclaratorSyntax variable when !IsConstant(variable) => variable.Initializer,
        PropertyDeclarationSyntax property => property.Initializer,
        _ => null,
    };

    /// <summary>The expression of the expression body, otherwise of the initialiser.</summary>
    private ExpressionSyntax? Value => ExpressionBody?.Expression ?? Initialiser?.Value;

    /// <summary>The calls in the member of a method with one of the names.</summary>
    public IEnumerable<InvocationExpressionSyntax> CallsOfMethodsNamed(IReadOnlyCollection<string> methodNames) => SyntaxNames.CallsIn(Node, methodNames);

    public static IReadOnlyList<WrittenMember> Of(IEnumerable<SourceFile> files) =>
        [.. files.SelectMany(file => Of(file.Path, file.Root))];

    public static IReadOnlyList<WrittenMember> Of(string sourcePath, SyntaxNode root) =>
    [
        .. CodeOutline.MembersOf(root).Select(member => new WrittenMember(
            sourcePath, member.Type, member.Name, SyntaxLines.FirstLineOf(member.Declaration), SyntaxLines.LastLineOf(member.Declaration), member.Node)),
    ];

    private static bool IsConstant(VariableDeclaratorSyntax variable) =>
        variable.Ancestors().OfType<BaseFieldDeclarationSyntax>().First().Modifiers.Any(SyntaxKind.ConstKeyword);

    /// <summary>
    /// Syntax by which a value decides or computes something: an operator other than a sum (a comparison, a logical, null or
    /// type test, arithmetic), a conditional, a null-conditional access, a pattern, a switch, a throw, a prefix operator (also
    /// the "-" of a negative number), an assignment (also in an object or <c>with</c> initialiser), a lambda or a call other than
    /// of a constructor.
    /// </summary>
    private static bool IsDeciding(SyntaxNode node) => node switch
    {
        BinaryExpressionSyntax binary => !binary.IsKind(SyntaxKind.AddExpression),
        ConditionalExpressionSyntax or ConditionalAccessExpressionSyntax or IsPatternExpressionSyntax or SwitchExpressionSyntax
            or ThrowExpressionSyntax or PrefixUnaryExpressionSyntax or AssignmentExpressionSyntax or AnonymousFunctionExpressionSyntax
            or InvocationExpressionSyntax => true,
        _ => false,
    };
}
