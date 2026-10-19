namespace AddedObjectRemover.Tests.LoadOrder;

public class LoadOrderPositionsTests
{
    private static readonly PluginName First = new("First.esm");
    private static readonly PluginName Second = new("Second.esp");

    [Fact]
    public void APluginLoadingLaterHasAHigherPositionWhateverTheLetterCase()
    {
        var positions = new LoadOrderPositions([new PluginListing(First, true, []), new PluginListing(Second, true, [])]);

        Assert.Equal((0, 1), (positions.Of(new PluginName("first.ESM")), positions.Of(Second)));
    }

    [Fact]
    public void APluginListedTwiceHasItsLastPosition() =>
        Assert.Equal(
            2,
            new LoadOrderPositions([new PluginListing(First, true, []), new PluginListing(Second, true, []), new PluginListing(First, true, [])]).Of(First));
}
