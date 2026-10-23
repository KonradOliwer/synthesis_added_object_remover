using AddedObjectRemover.Tests.EndToEnd;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

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

    [Fact]
    public void EveryEntryOfThePluginListHasItsListPositionWhetherOrNotTheStateLoadedIt()
    {
        var plugins = Create(Patch, First, Second, Third);

        Assert.Equal((0, 1, 2), (plugins.Positions.Of(First), plugins.Positions.Of(Second), plugins.Positions.Of(Third)));
        Assert.Equal(3, plugins.Positions.Count);
    }

    [Fact]
    public void ThePatchPositionIsTheIndexOfThePatchsEntryWhenItIsInTheMiddleOfTheList()
    {
        var plugins = Create(Patch, First, Patch, Second, Third);

        Assert.Equal(1, plugins.PatchPosition);
        Assert.True(plugins.Positions.Of(Second) > plugins.PatchPosition);
    }

    [Fact]
    public void ThePatchPositionIsTheEndOfTheListWhenThePatchHasNoEntry()
    {
        var plugins = Create(Patch, First, Second);

        Assert.Equal(2, plugins.PatchPosition);
    }

    [Fact]
    public void ThePatchsEntryIsFoundWhateverTheLetterCase()
    {
        var plugins = Create(new PluginName("PATCH.ESP"), First, Patch, Second);

        Assert.Equal(1, plugins.PatchPosition);
    }

    [Fact]
    public void TheDisabledEntriesAreTheOnesWithoutTheEnabledFlag()
    {
        var plugins = LoadOrderPluginsReader.Create(
            [],
            Patch,
            "Plugins.txt",
            [new PluginListEntry(First, Enabled: true), new PluginListEntry(Second, Enabled: false), new PluginListEntry(Third, Enabled: false)],
            []);

        Assert.Equal([Second, Third], plugins.DisabledPlugins.Order(PluginNameOrder));
        Assert.Contains(new PluginName("SECOND.esp"), plugins.DisabledPlugins);
        Assert.Equal(2, plugins.Positions.Of(Third));
    }

    [Fact]
    public void TheFixtureWorldReadsItsPositionsFromAListingSequenceWithTheModsEnabledAndThePatchWhereChosen()
    {
        var state = FixturePatcherState.Create(
            [new SkyrimMod(ModKey.FromNameAndExtension("Skyrim.esm"), SkyrimRelease.SkyrimSE), new SkyrimMod(ModKey.FromNameAndExtension("First.esp"), SkyrimRelease.SkyrimSE)],
            Path.GetTempPath(),
            Path.GetTempPath());

        using var world = FixturePatcherState.CreateFinalWorld(state, [new SkyrimMod(ModKey.FromNameAndExtension("Below.esp"), SkyrimRelease.SkyrimSE)]);

        Assert.Equal(2, world.Plugins.PatchPosition);
        Assert.Equal(1, world.Plugins.Positions.Of(new PluginName("First.esp")));
        Assert.Equal(3, world.Plugins.Positions.Of(new PluginName("Below.esp")));
        Assert.Empty(world.Plugins.DisabledPlugins);
    }

    private static readonly PluginName Third = new("Third.esp");
    private static readonly PluginName Patch = new("Patch.esp");

    private static readonly Comparer<PluginName> PluginNameOrder = Comparer<PluginName>.Create(
        (left, right) => string.Compare(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase));

    /// <param name="inListOrder">The entries of the plugin list, all enabled.</param>
    private static LoadOrderPlugins Create(PluginName patch, params PluginName[] inListOrder) =>
        LoadOrderPluginsReader.Create([], patch, "Plugins.txt", [.. inListOrder.Select(plugin => new PluginListEntry(plugin, Enabled: true))], []);
}
