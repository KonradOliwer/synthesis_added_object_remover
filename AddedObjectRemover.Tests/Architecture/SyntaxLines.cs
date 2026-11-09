using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>The 1-based source lines of syntax, as an editor numbers them.</summary>
internal static class SyntaxLines
{
    public static int FirstLineOf(SyntaxNode node) => OneBased(node.GetLocation().GetLineSpan().StartLinePosition);

    public static int LastLineOf(SyntaxNode node) => OneBased(node.GetLocation().GetLineSpan().EndLinePosition);

    public static int LineOf(SyntaxToken token) => OneBased(token.GetLocation().GetLineSpan().StartLinePosition);

    /// <summary>Roslyn numbers lines from 0.</summary>
    private static int OneBased(LinePosition position) => position.Line + 1;
}
