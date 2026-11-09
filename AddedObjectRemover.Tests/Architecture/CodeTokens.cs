using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>A token with the placeholder the scan compares (<see cref="Text"/>) and, for a word, the name as written.</summary>
internal readonly record struct CodeToken(string Text, int Line, string Word = "");

/// <summary>
/// Reads C# syntax into tokens for the copied-code scan. Comments and preprocessor lines are not tokens, every string or
/// character literal (an interpolated string with its holes included) is one token, names and numbers are replaced by
/// placeholders so a copy with renamed variables still matches. Lines that hold only braces, a using or an attribute are
/// dropped, because they repeat everywhere, and so are lists: three or more lines in a row with the same tokens (switch arms,
/// one item or one axis per line), whose items differ while their placeholders repeat.
/// </summary>
internal static class CodeTokens
{
    private const int MinLinesOfAList = 3;

    public const string Name = "name";
    private const string Number = "number";
    private const string Text = "text";

    /// <summary>A contextual keyword the parser keeps as a name; it stands where a type keyword stands, so it is kept as written.</summary>
    private static readonly string VarKeyword = SyntaxFacts.GetText(SyntaxKind.VarKeyword);

    private static readonly string UsingKeyword = SyntaxFacts.GetText(SyntaxKind.UsingKeyword);
    private static readonly string OpenParenthesis = SyntaxFacts.GetText(SyntaxKind.OpenParenToken);
    private static readonly string OpenBracket = SyntaxFacts.GetText(SyntaxKind.OpenBracketToken);
    private static readonly string CloseBracket = SyntaxFacts.GetText(SyntaxKind.CloseBracketToken);

    private static readonly string[] LineOnlyPunctuation = ["{", "}", ";", ","];

    /// <summary>The tokens that count for the scan: lines of braces, usings and attributes, and lists removed.</summary>
    public static IReadOnlyList<CodeToken> Significant(SyntaxNode root)
    {
        var lines = Tokenize(root).GroupBy(token => token.Line).OrderBy(group => group.Key)
            .Select(line => line.ToList())
            .Where(tokens => !IsSkippedLine(tokens))
            .ToList();
        return WithoutLists(lines).SelectMany(line => line).ToList();
    }

    public static IReadOnlyList<CodeToken> Tokenize(SyntaxNode root) =>
        root.DescendantNodesAndTokens(node => node is not InterpolatedStringExpressionSyntax)
            .Where(IsOneToken)
            .Select(item => item.IsToken ? CodeTokenOf(item.AsToken()) : new CodeToken(Text, SyntaxLines.FirstLineOf(item.AsNode()!)))
            .ToList();

    /// <summary>A token written in the source, or a whole interpolated string.</summary>
    private static bool IsOneToken(SyntaxNodeOrToken item) =>
        item.IsToken
            ? !item.AsToken().IsMissing && !item.IsKind(SyntaxKind.EndOfFileToken)
            : item.AsNode() is InterpolatedStringExpressionSyntax;

    private static CodeToken CodeTokenOf(SyntaxToken token)
    {
        var line = SyntaxLines.LineOf(token);
        if (token.IsKeyword() || token.Text == VarKeyword) return new CodeToken(token.Text, line, token.Text);
        if (token.IsKind(SyntaxKind.IdentifierToken)) return new CodeToken(Name, line, token.Text);
        if (token.IsKind(SyntaxKind.NumericLiteralToken)) return new CodeToken(Number, line);
        if (TextLiterals.IsTextToken(token)) return new CodeToken(Text, line);
        return new CodeToken(token.Text, line);
    }

    private static IEnumerable<List<CodeToken>> WithoutLists(List<List<CodeToken>> lines)
    {
        var shapes = lines.Select(line => string.Join(" ", line.Select(token => token.Text))).ToList();
        var start = 0;
        while (start < lines.Count)
        {
            var end = start + 1;
            while (end < lines.Count && shapes[end] == shapes[start]) end++;
            if (end - start < MinLinesOfAList)
            {
                for (var line = start; line < end; line++) yield return lines[line];
            }
            start = end;
        }
    }

    private static bool IsSkippedLine(List<CodeToken> tokens)
    {
        if (tokens.All(token => LineOnlyPunctuation.Contains(token.Text))) return true;
        if (tokens[0].Text == OpenBracket && tokens[^1].Text == CloseBracket) return true;
        return tokens[0].Text == UsingKeyword && tokens.Count > 1 && tokens[1].Text != VarKeyword && tokens[1].Text != OpenParenthesis;
    }
}
