namespace AddedObjectRemover.Tests.Determinism;

public class PermutationTests
{
    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { 2, 0, 1 })]
    public void APermutationIsAccepted(int[] order) => Permutation.Require([.. order], "order");

    [Theory]
    [InlineData(new[] { -1 })]
    [InlineData(new[] { 0, 0 })]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 0, 2 })]
    public void AnythingElseIsRejectedNamingTheParameter(int[] order)
    {
        var failure = Assert.Throws<ArgumentException>(() => Permutation.Require([.. order], "order"));

        Assert.Equal("order", failure.ParamName);
    }
}
