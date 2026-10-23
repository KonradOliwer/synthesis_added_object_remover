using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

public class ListingsRequiringTests
{
    private static readonly PluginName Base = new("Skyrim.esm");
    private static readonly PluginName Target = new("Target.esp");
    private static readonly PluginName Patch = new("Patch.esp");
    private static readonly PluginName Requiring = new("Requiring.esp");
    private static readonly PluginName Unreadable = new("Unreadable.esp");
    private static readonly PluginName Other = new("Other.esp");

    [Fact]
    public void ThePluginsThatListTheModAsAMasterAreGivenAndThePatchIsLeftOut()
    {
        var plugins = TestLoadOrderPlugins.Of(
            new KnownPluginNames([Base, Target, Patch, Requiring, Other]),
            Patch,
            [
                new PluginListing(Base, true, []),
                new PluginListing(Target, true, [Base]),
                new PluginListing(Patch, true, [Base, Target]),
                new PluginListing(Requiring, true, [Base, Target]),
                new PluginListing(Other, true, [Base]),
            ]);

        Assert.Equal([Requiring], plugins.ListingsRequiring(Target).Select(listing => listing.Mod));
    }

    [Fact]
    public void APluginThatCouldNotBeReadHasNoMastersAndIsNotGiven()
    {
        var plugins = TestLoadOrderPlugins.Of(
            new KnownPluginNames([Base, Target, Patch, Unreadable]),
            Patch,
            [
                new PluginListing(Base, true, []),
                new PluginListing(Target, true, [Base]),
                new PluginListing(Patch, true, []),
                new PluginListing(Unreadable, false, []),
            ]);

        Assert.Empty(plugins.ListingsRequiring(Target));
    }

    [Fact]
    public void ThePluginsAreGivenInTheOrderTheyAreListed()
    {
        var second = new PluginName("Second.esp");
        var plugins = TestLoadOrderPlugins.Of(
            new KnownPluginNames([Base, Target, Requiring, second, Patch]),
            Patch,
            [
                new PluginListing(Base, true, []),
                new PluginListing(Target, true, [Base]),
                new PluginListing(Requiring, true, [Base, Target]),
                new PluginListing(second, true, [Base, Target]),
                new PluginListing(Patch, true, []),
            ]);

        Assert.Equal([Requiring, second], plugins.ListingsRequiring(Target).Select(listing => listing.Mod));
    }
}
