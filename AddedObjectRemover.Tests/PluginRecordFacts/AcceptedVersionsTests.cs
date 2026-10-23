namespace AddedObjectRemover.Tests.PluginRecordFacts;

/// <summary>
/// Each test is an index table: the versions of one record from the lowest to the final one, each with its trust and
/// its value of the property (a version holds the final value when its value equals the last one).
/// </summary>
public class AcceptedVersionsTests
{
    private const VersionTrust Transparent = VersionTrust.Transparent;
    private const VersionTrust Full = VersionTrust.Full;
    private const VersionTrust OwnEdits = VersionTrust.OwnEdits;
    private const VersionTrust None = VersionTrust.None;

    private static Func<int, int, bool> DifferByMoreThan(int tolerance, int[] values) =>
        (first, second) => Math.Abs(values[first] - values[second]) > tolerance;

    private static PropertyAcceptance DecideOne(VersionTrust[] trusts, int[] values) =>
        AcceptedVersions.Decide(trusts, [DifferByMoreThan(0, values)])[0];

    [Fact]
    public void TheHighestFullyTrustedVersionIsAccepted()
    {
        // 0 Full 1, 1 Full 2, 2 None 3
        var result = DecideOne([Full, Full, None], [1, 2, 3]);

        Assert.Equal(new PropertyAcceptance(1, true, 2), result);
    }

    [Fact]
    public void AFullyTrustedWinnerIsAcceptedAndNothingChanged()
    {
        // 0 Full 1, 1 None 5, 2 Full 2
        var result = DecideOne([Full, None, Full], [1, 5, 2]);

        Assert.Equal(new PropertyAcceptance(2, false, null), result);
    }

    [Fact]
    public void AFullyTrustedVersionCarryingTheValueOfAnUntrustedOneAcceptsIt()
    {
        // 0 None 1, 1 Full 1
        var result = DecideOne([None, Full], [1, 1]);

        Assert.Equal(new PropertyAcceptance(1, false, null), result);
    }

    [Fact]
    public void AnUntrustedVersionAboveTheAcceptedOneThatKeepsTheValueChangesNothing()
    {
        // 0 Full 1, 1 Full 2, 2 None 2
        var result = DecideOne([Full, Full, None], [1, 2, 2]);

        Assert.Equal(new PropertyAcceptance(1, false, null), result);
    }

    [Fact]
    public void AnOwnEditsVersionThatCreatedTheRecordIsAccepted()
    {
        // 0 OwnEdits 1, 1 None 1
        var result = DecideOne([OwnEdits, None], [1, 1]);

        Assert.Equal(new PropertyAcceptance(0, false, null), result);
    }

    [Fact]
    public void AnOwnEditsVersionAloneIsAcceptedAsTheFinalVersion()
    {
        var result = DecideOne([OwnEdits], [1]);

        Assert.Equal(new PropertyAcceptance(0, false, null), result);
    }

    [Fact]
    public void AnUntrustedVersionChangingTheValueOfAnOwnEditsCreatedRecordIsTheChangingOne()
    {
        // 0 OwnEdits 1, 1 None 2
        var result = DecideOne([OwnEdits, None], [1, 2]);

        Assert.Equal(new PropertyAcceptance(0, true, 1), result);
    }

    [Fact]
    public void AnOwnEditsVersionThatDiffersFromTheVersionBelowIsAccepted()
    {
        // 0 Full 1, 1 OwnEdits 2
        var result = DecideOne([Full, OwnEdits], [1, 2]);

        Assert.Equal(new PropertyAcceptance(1, false, null), result);
    }

    [Fact]
    public void AnOwnEditsVersionIsComparedWithTheVersionJustBelowEvenWhenThatOneIsUntrusted()
    {
        // 0 Full 1, 1 None 3, 2 OwnEdits 2
        var result = DecideOne([Full, None, OwnEdits], [1, 3, 2]);

        Assert.Equal(new PropertyAcceptance(2, false, null), result);
    }

    [Fact]
    public void AnOwnEditsVersionThatOnlyCarriesTheValueOfTheVersionBelowOwnsNothing()
    {
        // 0 Full 1, 1 OwnEdits 1
        var result = DecideOne([Full, OwnEdits], [1, 1]);

        Assert.Equal(new PropertyAcceptance(0, false, null), result);
    }

    [Fact]
    public void AnOwnEditsVersionCarryingAnUntrustedValueHandsNothingOver()
    {
        // 0 Full 1, 1 None 2, 2 OwnEdits 2
        var result = DecideOne([Full, None, OwnEdits], [1, 2, 2]);

        Assert.Equal(new PropertyAcceptance(0, true, 1), result);
    }

    [Fact]
    public void AnOwnEditsVersionCarryingTheValueOfTheOnlyUntrustedVersionBelowLeavesNothingAccepted()
    {
        // 0 None 1, 1 OwnEdits 1
        var result = DecideOne([None, OwnEdits], [1, 1]);

        Assert.Equal(new PropertyAcceptance(null, true, 0), result);
    }

    [Fact]
    public void ALaterOwnEditsVersionCarryingTheValueUnchangedHandsNothingOver()
    {
        // 0 OwnEdits 1, 1 OwnEdits 1, 2 None 2
        var result = DecideOne([OwnEdits, OwnEdits, None], [1, 1, 2]);

        Assert.Equal(new PropertyAcceptance(0, true, 2), result);
    }

    [Fact]
    public void ALaterFullyTrustedVersionCarryingTheValueUnchangedIsStillTheAcceptedOne()
    {
        // 0 Full 1, 1 Full 1, 2 None 2
        var result = DecideOne([Full, Full, None], [1, 1, 2]);

        Assert.Equal(new PropertyAcceptance(1, true, 2), result);
    }

    [Fact]
    public void WithNoTrustedVersionNothingIsAcceptedAndTheValueCounts()
    {
        // 0 None 1, 1 None 2
        var result = DecideOne([None, None], [1, 2]);

        Assert.Equal(new PropertyAcceptance(null, true, 1), result);
    }

    [Fact]
    public void WithNoTrustedVersionTheLowestVersionHoldingTheFinalValueIsTheChangingOne()
    {
        // 0 None 1, 1 None 2, 2 None 2
        var result = DecideOne([None, None, None], [1, 2, 2]);

        Assert.Equal(new PropertyAcceptance(null, true, 1), result);
    }

    [Fact]
    public void ARecordOfNoVersionsHasNothingAcceptedAndNoChangingVersion()
    {
        var result = DecideOne([], []);

        Assert.Equal(new PropertyAcceptance(null, true, null), result);
    }

    [Fact]
    public void ATransparentVersionIsNeverAccepted()
    {
        // 0 Full 1, 1 Transparent 2
        var result = DecideOne([Full, Transparent], [1, 2]);

        Assert.Equal(new PropertyAcceptance(0, true, null), result);
    }

    [Fact]
    public void ATransparentWinnerHoldingTheFinalValueIsSkippedForTheChangingVersion()
    {
        // 0 Full 1, 1 None 2, 2 Transparent 2
        var result = DecideOne([Full, None, Transparent], [1, 2, 2]);

        Assert.Equal(new PropertyAcceptance(0, true, 1), result);
    }

    [Fact]
    public void WhenOnlyATransparentVersionHoldsTheFinalValueThereIsNoChangingVersion()
    {
        // 0 Full 1, 1 None 3, 2 Transparent 2
        var result = DecideOne([Full, None, Transparent], [1, 3, 2]);

        Assert.Equal(new PropertyAcceptance(0, true, null), result);
    }

    [Fact]
    public void ATransparentVersionAloneHasNothingAcceptedAndNoChangingVersion()
    {
        var result = DecideOne([Transparent], [1]);

        Assert.Equal(new PropertyAcceptance(null, true, null), result);
    }

    [Fact]
    public void ATransparentVersionIsNotTheVersionJustBelowAnotherOne()
    {
        // 0 Full 1, 1 Transparent 9, 2 OwnEdits 1
        var result = DecideOne([Full, Transparent, OwnEdits], [1, 9, 1]);

        Assert.Equal(new PropertyAcceptance(0, false, null), result);
    }

    [Fact]
    public void AnOwnEditsVersionWithOnlyTransparentVersionsBelowCreatedTheRecord()
    {
        // 0 Transparent 9, 1 OwnEdits 9
        var result = DecideOne([Transparent, OwnEdits], [9, 9]);

        Assert.Equal(new PropertyAcceptance(1, false, null), result);
    }

    [Fact]
    public void TheChangingVersionIsTheLowestOneAboveTheAcceptedOneThatHoldsTheFinalValue()
    {
        // 0 Full 1, 1 None 2, 2 None 3, 3 None 3
        var result = DecideOne([Full, None, None, None], [1, 2, 3, 3]);

        Assert.Equal(new PropertyAcceptance(0, true, 2), result);
    }

    [Fact]
    public void VersionsBelowTheAcceptedOneAreNeverTheChangingVersion()
    {
        // 0 None 3, 1 Full 1, 2 None 3
        var result = DecideOne([None, Full, None], [3, 1, 3]);

        Assert.Equal(new PropertyAcceptance(1, true, 2), result);
    }

    [Fact]
    public void TheFinalVersionIsTheChangingOneWhenNoVersionBelowHoldsItsValue()
    {
        // 0 Full 1, 1 None 2, 2 None 3
        var result = DecideOne([Full, None, None], [1, 2, 3]);

        Assert.Equal(new PropertyAcceptance(0, true, 2), result);
    }

    [Fact]
    public void EachPropertyIsDecidedByItsOwnValues()
    {
        VersionTrust[] trusts = [Full, None, OwnEdits];
        int[] carriedOver = [1, 2, 2];
        int[] editedByOwnEdits = [1, 1, 3];
        int[] untouched = [1, 1, 1];

        var results = AcceptedVersions.Decide(
            trusts, [DifferByMoreThan(0, carriedOver), DifferByMoreThan(0, editedByOwnEdits), DifferByMoreThan(0, untouched)]);

        PropertyAcceptance[] expected = [new(0, true, 1), new(2, false, null), new(0, false, null)];
        Assert.Equal(expected, results);
    }

    [Fact]
    public void NoPropertiesGiveNoResults() =>
        Assert.Empty(AcceptedVersions.Decide([Full], []));

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void AFinalValueWithinTheToleranceOfTheAcceptedOneIsNotChanged(int tolerance, bool expectedChanged)
    {
        // 0 Full 10, 1 None 12
        var result = AcceptedVersions.Decide([Full, None], [DifferByMoreThan(tolerance, [10, 12])])[0];

        Assert.Equal(expectedChanged, result.Changed);
    }

    [Fact]
    public void AnOwnEditsVersionWithinTheToleranceOfTheVersionBelowOwnsNothing()
    {
        // 0 Full 10, 1 OwnEdits 11, tolerance 2
        var result = AcceptedVersions.Decide([Full, OwnEdits], [DifferByMoreThan(2, [10, 11])])[0];

        Assert.Equal(new PropertyAcceptance(0, false, null), result);
    }

    [Fact]
    public void AnOwnEditsVersionBeyondTheToleranceOfTheVersionBelowOwnsTheProperty()
    {
        // 0 Full 10, 1 OwnEdits 13, tolerance 2
        var result = AcceptedVersions.Decide([Full, OwnEdits], [DifferByMoreThan(2, [10, 13])])[0];

        Assert.Equal(new PropertyAcceptance(1, false, null), result);
    }

    [Fact]
    public void AVersionHoldsTheFinalValueWhenItIsWithinTheTolerance()
    {
        // 0 Full 10, 1 None 20, 2 None 21, tolerance 2
        var result = AcceptedVersions.Decide([Full, None, None], [DifferByMoreThan(2, [10, 20, 21])])[0];

        Assert.Equal(new PropertyAcceptance(0, true, 1), result);
    }

    [Fact]
    public void AVersionWithinTheToleranceOfTheAcceptedValueIsNotTheChangingVersion()
    {
        // 0 Full 10, 1 None 11, 2 None 20, tolerance 2
        var result = AcceptedVersions.Decide([Full, None, None], [DifferByMoreThan(2, [10, 11, 20])])[0];

        Assert.Equal(new PropertyAcceptance(0, true, 2), result);
    }
}
