using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The decision list's working copy is the way a step tries rounds without changing the shared list. Only the code that
/// decides rounds (the rule module of objects resting on removed ones and of other mods' objects left without support, the rule
/// module of objects left behind, which decides the one round of the steps for objects in cleared areas and for whole groups, the
/// steps for visible and invisible objects left behind, and the restore step, which records what goes back on a copy for the
/// follow-up rule) uses it; the decision list's own projects define it.
/// </summary>
public class WorkingCopyUseTests
{
    private static readonly string[] DecisionListProjects = ["RemovalDecisionList", "RemovalDecisionList.Contracts"];
    private static readonly string[] RoundDecidingCode =
    [
        "Rules/ObjectsRestingOnRemovedOnes", "Rules/ObjectsLeftBehind", "Steps/RemoveVisibleObjectsLeftBehind", "Steps/RemoveInvisibleObjectsLeftBehind",
        "Steps/RestoreWhereRemoved",
    ];

    [Fact]
    public void OnlyTheRoundDecidingStepsUseTheWorkingCopyOrDecideRounds()
    {
        var users = SourceFile.All()
            .Where(file => !file.IsIn(DecisionListProjects) && !file.IsIn(RoundDecidingCode))
            .SelectMany(file => FindUses(file.Code).Select(use => $"{file.Description}: {use}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            users.Count == 0,
            $"Only the round-deciding steps may use the working copy or decide rounds:{Environment.NewLine}{string.Join(Environment.NewLine, users)}");
    }

    [Fact]
    public void TheScanFindsTheUsesInTheRoundDecidingStepsAndTheDecisionListItself()
    {
        var uses = SourceFile.All()
            .Where(file => FindUses(file.Code).Count > 0)
            .Select(file => file.Project)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "ObjectsLeftBehind", "ObjectsRestingOnRemovedOnes", "RemovalDecisionList", "RemovalDecisionList.Contracts", "RemoveInvisibleObjectsLeftBehind",
                "RemoveVisibleObjectsLeftBehind", "RestoreWhereRemoved",
            ],
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
