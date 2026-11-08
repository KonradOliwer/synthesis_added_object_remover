namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The runner and its run caches print and build no user-visible text: the wording lives in the report step. Every string
/// literal left in them is listed here with its reason.
/// </summary>
public class RunnerWordingTests
{
    /// <summary>The literal as written between its quotes, and why it is not user-visible wording.</summary>
    private static readonly Dictionary<string, string> AllowedLiterals = new()
    {
        ["The NPC {npc} that supplies its own traits was not found."] = "Message of an InvalidOperationException for a bug in the caches.",
    };

    [Fact]
    public void TheRunnerHoldsNoUserVisibleText()
    {
        var found = RunnerRulesTests.RunnerFiles().SelectMany(file => file.StringLiterals).ToHashSet();

        var notAllowed = found.Except(AllowedLiterals.Keys).Order(StringComparer.Ordinal).ToList();
        var stale = AllowedLiterals.Keys.Except(found).Order(StringComparer.Ordinal).ToList();

        Assert.True(notAllowed.Count == 0, $"Text in the runner project; move the wording to the report step:{Environment.NewLine}{string.Join(Environment.NewLine, notAllowed)}");
        AllowListAssert.NoneStale(stale, $"Allowed in {nameof(RunnerWordingTests)} but no longer in the runner or the run caches (remove)");
    }
}
