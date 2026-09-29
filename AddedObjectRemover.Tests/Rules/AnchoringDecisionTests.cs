namespace AddedObjectRemover.Tests.Rules;

public class AnchoringDecisionTests
{
    [Theory]
    [InlineData(0.5f, 0.5f, true)]
    [InlineData(0.499995f, 0.5f, true)]
    [InlineData(0.4999f, 0.5f, false)]
    [InlineData(1f, 1f, true)]
    [InlineData(0.999995f, 1f, true)]
    [InlineData(0f, 0.01f, false)]
    public void RemovedWhenRemovedShareReachesThreshold(float removedShare, float threshold, bool expected) =>
        Assert.Equal(expected, SupportRule.ReachesThreshold(removedShare, threshold));

    [Fact]
    public void PointTouchingSeveralSupportersSplitsItsWeight()
    {
        var first = Supporter.Target(3);
        var second = Supporter.Placed(0);
        var contacts = AnchoringContactFinder.SplitWeightsAmongSupporters(
            [
                new AnchoringContactFinder.SupporterHits(first, [true, true, false]),
                new AnchoringContactFinder.SupporterHits(second, [false, true, false]),
                new AnchoringContactFinder.SupporterHits(Supporter.Terrain, [false, false, false]),
            ],
            [1f, 2f, 4f]);

        Assert.Equal(2, contacts.ContactPoints);
        Assert.Equal(3f, contacts.TotalWeight, 1e-6f);
        Assert.Equal(new[] { new SupporterWeight(first, 2f), new SupporterWeight(second, 1f) }, contacts.Supporters);
    }

    [Fact]
    public void NoContactsGiveNoWeight()
    {
        var contacts = AnchoringContactFinder.SplitWeightsAmongSupporters(
            [new AnchoringContactFinder.SupporterHits(Supporter.Terrain, [false, false])],
            [1f, 1f]);
        Assert.Equal(0, contacts.ContactPoints);
        Assert.Empty(contacts.Supporters);
    }
}
