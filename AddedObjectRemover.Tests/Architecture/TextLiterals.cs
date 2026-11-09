using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// Which syntax is a text literal (a string literal of any form, plain, verbatim, raw or UTF-8, a character literal, or an
/// interpolated string), the text each holds, and which string literals could be wording.
/// </summary>
internal static class TextLiterals
{
    private static readonly SyntaxKind[] StringTokenKinds =
    [
        SyntaxKind.StringLiteralToken, SyntaxKind.SingleLineRawStringLiteralToken, SyntaxKind.MultiLineRawStringLiteralToken,
        SyntaxKind.Utf8StringLiteralToken, SyntaxKind.Utf8SingleLineRawStringLiteralToken, SyntaxKind.Utf8MultiLineRawStringLiteralToken,
    ];

    /// <summary>The exceptions .NET gives for a bug (a wrong state, an unreachable branch, a wrong argument), whose message is never log wording.</summary>
    private static readonly string[] StandardBugExceptions =
        ["InvalidOperationException", "UnreachableException", "ArgumentException", "ArgumentNullException", "ArgumentOutOfRangeException"];

    public static bool IsTextToken(SyntaxToken token) => token.IsKind(SyntaxKind.CharacterLiteralToken) || IsStringToken(token);

    /// <summary>Every string literal and interpolated string; a literal inside an interpolation hole is part of its interpolated string.</summary>
    public static IEnumerable<ExpressionSyntax> StringLiteralsIn(SyntaxNode root) =>
        root.DescendantNodes(node => node is not InterpolatedStringExpressionSyntax)
            .OfType<ExpressionSyntax>()
            .Where(node => node is InterpolatedStringExpressionSyntax || (node is LiteralExpressionSyntax literal && IsStringToken(literal.Token)));

    /// <summary>
    /// The value of a literal of one token; of an interpolated string, its text as written between its opening and closing quotes,
    /// the holes as written.
    /// </summary>
    public static string TextOf(ExpressionSyntax literal) => literal switch
    {
        LiteralExpressionSyntax plain => plain.Token.ValueText,
        InterpolatedStringExpressionSyntax interpolated =>
            interpolated.ToString()[interpolated.StringStartToken.Text.Length..^interpolated.StringEndToken.Text.Length],
        _ => throw new ArgumentException($"Not a text literal: {literal}", nameof(literal)),
    };

    /// <summary>
    /// Each string literal that could be wording, with the line it starts on: every literal except those inside a thrown
    /// <see cref="StandardBugExceptions"/>. The project's own exception types are kept, because the settings reading words user failures with them.
    /// </summary>
    public static IReadOnlyList<(string Literal, int Line)> WordingLiteralLinesIn(SyntaxNode root) =>
        [.. StringLiteralsIn(root).Where(literal => !IsInThrownStandardBugException(literal)).Select(literal => (TextOf(literal), SyntaxLines.FirstLineOf(literal)))];

    private static bool IsStringToken(SyntaxToken token) => StringTokenKinds.Contains(token.Kind());

    private static bool IsInThrownStandardBugException(SyntaxNode literal) =>
        literal.Ancestors().OfType<ObjectCreationExpressionSyntax>().Any(creation =>
            StandardBugExceptions.Contains(SyntaxNames.RightmostName(creation.Type)) && creation.Parent is ThrowStatementSyntax or ThrowExpressionSyntax);
}
