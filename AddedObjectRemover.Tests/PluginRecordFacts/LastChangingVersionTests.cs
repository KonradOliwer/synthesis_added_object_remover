namespace AddedObjectRemover.Tests.PluginRecordFacts;

/// <summary>Each test is an index table: the value of one part in each version, lowest first.</summary>
public class LastChangingVersionTests
{
    private static int? LastChanger(params int[] values) =>
        LastChangingVersion.Of(values.Length, (below, index) => values[below] != values[index]);

    [Fact]
    public void TheHighestVersionThatDiffersFromTheOneBelowIsTheLastChanger() =>
        Assert.Equal(2, LastChanger(1, 2, 3));

    [Fact]
    public void AVersionCarryingTheValueOverIsNotAChanger() =>
        Assert.Equal(1, LastChanger(1, 2, 2, 2));

    [Fact]
    public void ARevertToAnEarlierValueIsAChange() =>
        Assert.Equal(2, LastChanger(1, 2, 1));

    [Fact]
    public void NoVersionDiffersFromTheOneBelowWhenAllValuesAreEqual() =>
        Assert.Null(LastChanger(4, 4, 4));

    [Fact]
    public void ASingleVersionHasNoChanger() =>
        Assert.Null(LastChanger(7));

    [Fact]
    public void NoVersionsHaveNoChanger() =>
        Assert.Null(LastChangingVersion.Of(0, (_, _) => true));
}
