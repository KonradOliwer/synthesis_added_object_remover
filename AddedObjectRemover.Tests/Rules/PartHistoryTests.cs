using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// Each test is an index table: the plugins of the versions of one record from the lowest, with the value of one part in each.
/// Version 0 lies just below the cleaned mod, which is at index 1 unless a test names another; the last version is the winner.
/// CompatPatch.esp and Later.esp are real plugins that load without the cleaned mod; Needing.esp needs the cleaned mod; Patch.esp is
/// the patch, which holds what earlier patchers of the group wrote.
/// </summary>
public class PartHistoryTests
{
    private const float Tolerance = 0.01f;

    private static readonly PluginName Base = new("Base.esm");
    private static readonly PluginName Cleaned = new("Cleaned.esp");
    private static readonly PluginName Later = new("Later.esp");
    private static readonly PluginName Compat = new("CompatPatch.esp");
    private static readonly PluginName Needing = new("Needing.esp");
    private static readonly PluginName Patch = new("Patch.esp");

    private static readonly PluginName OtherGroupPatch = new("OtherGroupPatch.esp");

    private static readonly ModTrustTable Trust = TestModTrust.Of(
        Cleaned, Patch, trusted: [Base, Later, Compat], untrusted: [], needingCleanedMod: [Needing], otherGroupPatches: [OtherGroupPatch]);

    private static PartOutcome Outcome(PluginName[] plugins, params int[] values) => OutcomeWithCleanedModAt(plugins, cleanedModIndex: 1, values);

    private static PartOutcome OutcomeWithCleanedModAt(PluginName[] plugins, int cleanedModIndex, params int[] values) =>
        PartHistory.Of(plugins, cleanedModIndex, Trust, (first, second) => values[first] != values[second]);

    private static PartOutcome OutcomeWithinTolerance(PluginName[] plugins, params float[] values) =>
        PartHistory.Of(plugins, cleanedModIndex: 1, Trust, (first, second) => Math.Abs(values[first] - values[second]) > Tolerance);

    /// <summary>The same table, read as the reader compares a record's versions and judged from those comparisons.</summary>
    private static PartOutcome OutcomeOfComparedVersions(PluginName[] plugins, params int[] values) =>
        PartHistory.Of(
            VersionComparison.ComparePart(plugins, (first, second) => values[first] != values[second], lowestNotCopyable: false), Trust);

    [Fact]
    public void ThePartIsUndoneWhenTheCleanedModIsTheLastToChangeIt() =>
        Assert.Equal(new PartOutcome.Undo(), Outcome([Base, Cleaned], 1, 2));

    /// <summary>Later.esp loads without the cleaned mod, so it was made against the base version: holding the cleaned mod's value, it set that value.</summary>
    [Fact]
    public void ThePartIsLeftToALaterPluginThatLoadsWithoutTheCleanedModAndHoldsItsValue() =>
        Assert.Equal(new PartOutcome.LeftToLaterPlugin(Later), Outcome([Base, Cleaned, Later, Patch], 1, 2, 2, 2));

    [Fact]
    public void ThePartIsUndoneWhenOnlyAPluginThatNeedsTheCleanedModAndThePatchCarryTheValueOver() =>
        Assert.Equal(new PartOutcome.Undo(), Outcome([Base, Cleaned, Needing, Patch], 1, 2, 2, 2));

    /// <summary>Needing.esp would not load without the cleaned mod, so its change keeps nothing where the cleaned mod is gone.</summary>
    [Fact]
    public void ThePartIsUndoneWhenThePluginThatChangedItAgainNeedsTheCleanedMod() =>
        Assert.Equal(new PartOutcome.Undo(), Outcome([Base, Cleaned, Needing], 1, 2, 3));

    /// <summary>Later.esp holds the value Needing.esp gave the part, without needing the cleaned mod: it set that value.</summary>
    [Fact]
    public void ThePartIsLeftToAPluginThatLoadsWithoutTheCleanedModAboveOneThatNeedsIt() =>
        Assert.Equal(new PartOutcome.LeftToLaterPlugin(Later), Outcome([Base, Cleaned, Needing, Later], 1, 2, 3, 3));

    /// <summary>Later.esp's own value lies below Needing.esp's change and cannot be written back, so the part stays as it is.</summary>
    [Fact]
    public void ThePartStaysWithAPluginThatNeedsTheCleanedModAboveOneThatLoadsWithoutIt() =>
        Assert.Equal(new PartOutcome.LeftToLaterPlugin(Needing), Outcome([Base, Cleaned, Later, Needing], 1, 2, 3, 4));

    /// <summary>Another group's patch loads without the cleaned mod, but it is a copy of what was in the winning record: it carried the value over.</summary>
    [Fact]
    public void ThePartIsUndoneWhenOnlyAnotherGroupsPatchHoldsTheCleanedModsValue() =>
        Assert.Equal(new PartOutcome.Undo(), Outcome([Base, Cleaned, OtherGroupPatch], 1, 2, 2));

    [Fact]
    public void ThePartIsLeftToAnotherGroupsPatchThatChangedItAgain() =>
        Assert.Equal(new PartOutcome.LeftToLaterPlugin(OtherGroupPatch), Outcome([Base, Cleaned, OtherGroupPatch], 1, 2, 3));

    [Fact]
    public void ThePartIsUndoneWhenAnotherGroupsPatchSetsItBackToTheCleanedModsValue() =>
        Assert.Equal(new PartOutcome.Undo(), Outcome([Base, Cleaned, Later, OtherGroupPatch], 1, 2, 3, 2));

    [Fact]
    public void ThePartIsLeftToARealPluginBelowAnotherGroupsPatchThatCarriesItsValueOver() =>
        Assert.Equal(new PartOutcome.LeftToLaterPlugin(Later), Outcome([Base, Cleaned, Later, OtherGroupPatch], 1, 2, 2, 2));

    [Fact]
    public void ThePartIsUndoneWhenTheCleanedModRevertsToAnEarlierValue() =>
        Assert.Equal(new PartOutcome.Undo(), OutcomeWithCleanedModAt([Base, Later, Cleaned], cleanedModIndex: 2, 1, 2, 1));

    [Fact]
    public void ThePartIsUndoneWhenALaterPluginMovesItAwayAndThePatchSetsItBackToTheCleanedModsValue() =>
        Assert.Equal(new PartOutcome.Undo(), Outcome([Base, Cleaned, Later, Patch], 1, 2, 3, 2));

    [Fact]
    public void ThePartIsLeftToARealPluginThatSetsItBackToTheCleanedModsValue() =>
        Assert.Equal(new PartOutcome.LeftToLaterPlugin(Compat), Outcome([Base, Cleaned, Later, Compat], 1, 2, 3, 2));

    [Fact]
    public void ThePartIsLeftToARealPluginThatSetsItBackToTheCleanedModsValueWhenThePatchCarriesItOver() =>
        Assert.Equal(new PartOutcome.LeftToLaterPlugin(Compat), Outcome([Base, Cleaned, Later, Compat, Patch], 1, 2, 3, 2, 2));

    [Fact]
    public void ThePartIsLeftToThePatchWhenItSetsAValueThatDiffersFromTheCleanedModsAfterAnotherPluginSetItBack() =>
        Assert.Equal(new PartOutcome.LeftToLaterPlugin(Patch), Outcome([Base, Cleaned, Later, Compat, Patch], 1, 2, 3, 2, 4));

    [Fact]
    public void ThePartIsLeftToTheLaterPluginThatChangedItAgain() =>
        Assert.Equal(new PartOutcome.LeftToLaterPlugin(Later), Outcome([Base, Cleaned, Later], 1, 2, 3));

    [Fact]
    public void ThePartIsLeftToTheLastOfSeveralLaterChangers() =>
        Assert.Equal(new PartOutcome.LeftToLaterPlugin(Compat), Outcome([Base, Cleaned, Later, Compat], 1, 2, 3, 4));

    [Fact]
    public void ThePartIsLeftToALaterPluginThatRevertsToTheValueBelowTheCleanedMod() =>
        Assert.Equal(new PartOutcome.LeftToLaterPlugin(Later), Outcome([Base, Cleaned, Later], 1, 2, 1));

    [Fact]
    public void ThePatchChangingThePartAgainMakesItTheLastChanger() =>
        Assert.Equal(new PartOutcome.LeftToLaterPlugin(Patch), Outcome([Base, Cleaned, Later, Patch], 1, 2, 2, 5));

    [Fact]
    public void ThePartIsUnchangedWhenTheCleanedModOnlyCarriesTheValueOver() =>
        Assert.Equal(new PartOutcome.Unchanged(), Outcome([Base, Cleaned], 1, 1));

    [Fact]
    public void ThePartIsUnchangedWhenTheCleanedModCarriesTheValueOverAndALaterPluginChangesIt() =>
        Assert.Equal(new PartOutcome.Unchanged(), Outcome([Base, Cleaned, Later], 1, 1, 2));

    /// <summary>Each later step stays within the tolerance, but together they move the value past it: the last of them changed the part.</summary>
    [Fact]
    public void StepsWithinTheToleranceThatAddUpToAChangeLeaveThePartToTheLastPluginThatMovedIt() =>
        Assert.Equal(new PartOutcome.LeftToLaterPlugin(Compat), OutcomeWithinTolerance([Base, Cleaned, Later, Compat], 0f, 5f, 5.009f, 5.018f));

    /// <summary>Each later step stays within the tolerance of the cleaned mod's value, so the last plugin that loads without it holds that value.</summary>
    [Fact]
    public void StepsWithinTheToleranceThatStayWithinItOfTheCleanedModsValueLeaveThePartToTheLastPluginHoldingIt() =>
        Assert.Equal(new PartOutcome.LeftToLaterPlugin(Compat), OutcomeWithinTolerance([Base, Cleaned, Later, Compat], 0f, 5f, 5.004f, 5.008f));

    [Fact]
    public void StepsWithinTheToleranceOfAPluginThatNeedsTheCleanedModLeaveThePartToUndo() =>
        Assert.Equal(new PartOutcome.Undo(), OutcomeWithinTolerance([Base, Cleaned, Needing], 0f, 5f, 5.004f));

    [Theory]
    [InlineData(new[] { 1, 2, 3, 2 })]
    [InlineData(new[] { 1, 2, 3, 4, 2 })]
    [InlineData(new[] { 1, 2, 2, 5 })]
    [InlineData(new[] { 1, 1, 2 })]
    public void TheComparedVersionsGiveTheSameOutcomeAsTheValues(int[] values)
    {
        PluginName[] plugins = [Base, Cleaned, .. Enumerable.Range(0, values.Length - 3).Select(index => new PluginName($"Later{index}.esp")), Patch];

        Assert.Equal(Outcome(plugins, values), OutcomeOfComparedVersions(plugins, values));
    }
}
