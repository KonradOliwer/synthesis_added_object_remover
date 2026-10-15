using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.LoadOrder;

public class PluginNamesTests
{
    [Fact]
    public void RecordKeysPrintThePluginAsTheLoadOrderSpellsIt()
    {
        var names = new PluginNames(new KnownPluginNames([new PluginName("foo.esp")]));

        var key = names.KeyOf(new FormKey(ModKey.FromNameAndExtension("Foo.esp"), 0x800));

        Assert.Equal("000800:foo.esp", key.ToString());
    }

    [Fact]
    public void RecordKeysOfAPluginMissingFromTheLoadOrderAreABug()
    {
        var names = new PluginNames(new KnownPluginNames([new PluginName("foo.esp")]));

        var failure = Assert.Throws<InvalidOperationException>(() => names.KeyOf(new FormKey(ModKey.FromNameAndExtension("Bar.esp"), 0x800)));

        Assert.Contains("Bar.esp", failure.Message);
    }

    [Fact]
    public void NoneLinkIsTheNoneRecordKeyAndPrintsAsNone()
    {
        var names = new PluginNames(new KnownPluginNames([]));

        var key = names.KeyOf(FormKey.None);

        Assert.Equal(RecordKey.None, key);
        Assert.NotEqual(RecordKey.Null, key);
        Assert.Equal("None", key.ToString());
    }

    [Fact]
    public void NullLinkIsTheNullRecordKeyAndPrintsAsNull()
    {
        var names = new PluginNames(new KnownPluginNames([]));

        var key = names.KeyOf(FormKey.Null);

        Assert.Equal(RecordKey.Null, key);
        Assert.Equal("Null", key.ToString());
    }
}
