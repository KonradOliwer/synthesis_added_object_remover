using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>The names a piece of C# syntax refers to, as written, and the syntax found by them.</summary>
internal static class SyntaxNames
{
    /// <summary>The name of the method a call invokes (<c>Name(</c>, <c>receiver.Name(</c>, <c>?.Name(</c>, <c>Name&lt;T&gt;(</c>); null for a call of anything else, such as a delegate returned by a call.</summary>
    public static SyntaxToken? CalledNameToken(InvocationExpressionSyntax call) => call.Expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier,
        SimpleNameSyntax name => name.Identifier,
        _ => null,
    };

    public static string? CalledName(InvocationExpressionSyntax call) => CalledNameToken(call)?.Text;

    /// <summary>The last name of a type as written, without its namespace or type arguments: <c>Diagnostics.UnreachableException</c> is "UnreachableException".</summary>
    public static string RightmostName(TypeSyntax type) => type switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
        AliasQualifiedNameSyntax aliased => aliased.Name.Identifier.Text,
        SimpleNameSyntax name => name.Identifier.Text,
        _ => type.ToString(),
    };

    /// <summary>The expression is the name, or a qualified name that ends in it: <c>Console</c>, <c>System.Console</c>, <c>global::System.Console</c>.</summary>
    public static bool EndsWithName(ExpressionSyntax expression, string name) => expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.Text == name,
        TypeSyntax type => RightmostName(type) == name,
        _ => false,
    };

    public static bool IsIdentifier(ExpressionSyntax expression, string name) =>
        expression is IdentifierNameSyntax identifier && identifier.Identifier.Text == name;

    public static IEnumerable<SyntaxToken> IdentifierTokensIn(SyntaxNode node) =>
        node.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken));

    /// <summary>The calls below the node of a method with one of the names.</summary>
    public static IEnumerable<InvocationExpressionSyntax> CallsIn(SyntaxNode node, IReadOnlyCollection<string> methodNames) =>
        node.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(call => CalledName(call) is { } name && methodNames.Contains(name));

    /// <summary>The node is inside the arguments of a call written <c>receiver.method(...)</c>, the receiver also qualified.</summary>
    public static bool IsInArgumentsOfCallTo(SyntaxNode node, string receiver, string method) =>
        node.Ancestors().OfType<InvocationExpressionSyntax>().Any(outer =>
            outer.Expression is MemberAccessExpressionSyntax access
                && EndsWithName(access.Expression, receiver)
                && access.Name.Identifier.Text == method
                && outer.ArgumentList.Span.Contains(node.Span));
}
