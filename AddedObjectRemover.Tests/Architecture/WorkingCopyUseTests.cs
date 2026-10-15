using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The decision list's working copy is the way a step tries rounds without changing the shared list. Only the two steps
/// that decide rounds (resting objects and invisible objects left behind) use it; the decision list's own projects define it.
/// </summary>
public class WorkingCopyUseTests
{
    private static readonly string[] DecisionListProjects = ["RemovalDecisionList", "RemovalDecisionList.Contracts"];
    private static readonly string[] RoundDecidingSteps = ["Steps/RemoveObjectsRestingOnRemovedOnes", "Steps/RemoveInvisibleObjectsLeftBehind"];

    [Fact]
    public void OnlyTheRestingAndLeftBehindStepsUseTheWorkingCopyOrDecideRounds()
    {
        var users = SourceFile.All()
            .Where(file => !file.IsIn(DecisionListProjects) && !file.IsIn(RoundDecidingSteps))
            .SelectMany(file => FindUses(file.Code).Select(use => $"{file.Description}: {use}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            users.Count == 0,
            $"Only the resting-objects and left-behind steps may use the working copy or decide rounds:{Environment.NewLine}{string.Join(Environment.NewLine, users)}");
    }

    [Fact]
    public void TheScanFindsTheUsesInTheTwoStepsAndNotTheDecisionListItself()
    {
        var uses = SourceFile.All()
            .Where(file => FindUses(file.Code).Count > 0)
            .Select(file => file.Project)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            ["RemovalDecisionList", "RemovalDecisionList.Contracts", "RemoveInvisibleObjectsLeftBehind", "RemoveObjectsRestingOnRemovedOnes"],
            uses);
    }

    [Fact]
    public void TheUseReaderSeesTheWorkingCopyAndBothRoundDeciders()
    {
        const string code = """
            var a = decisions.WorkingCopy();
            copy.DecideRound(kind, proposals);
            copy.DecideRoundsUntilNothingRemoved(rounds);
            var b = DecideOnWorkingCopy(plan);
            """;

        Assert.Equal(["DecideRound", "WorkingCopy("], FindUses(code).Order(StringComparer.Ordinal));
    }

    private static IReadOnlyList<string> FindUses(string code) =>
        [.. Regex.Matches(code, @"\bWorkingCopy\s*\(|\bDecideRound").Select(match => Regex.Replace(match.Value, @"\s+", "")).Distinct()];
}
