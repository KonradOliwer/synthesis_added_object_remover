namespace AddedObjectRemover.Tests.LoadOrder;

public class ModTableTests
{
    [Fact]
    public void NamesDifferingOnlyInCaseAreOnePluginSpelledAsFirstSeen()
    {
        var table = new KnownPluginNames([new PluginName("Skyrim.esm"), new PluginName("Patch.esp"), new PluginName("skyrim.ESM")]);

        Assert.True(table.Knows(new PluginName("SKYRIM.ESM")));
        Assert.Equal("Skyrim.esm", table.WithKnownSpelling(new PluginName("skyrim.esm")).FileName);
        Assert.Equal("Skyrim.esm", table.WithKnownSpelling(new PluginName("skyrim.ESM")).FileName);
        Assert.False(table.Knows(new PluginName("Other.esp")));
    }

    [Fact]
    public void MastersAreSpelledAsTheLoadOrderListsThem()
    {
        var facts = LoadOrderPluginsReader.Create(
            [
                (new PluginName("Skyrim.esm"), []),
                (new PluginName("Target.esp"), [new PluginName("skyrim.ESM"), new PluginName("Unlisted.esm")]),
            ],
            new PluginName("Patch.esp"));

        var masters = facts.Find(new PluginName("TARGET.esp"))!.Masters;

        Assert.Equal(["Skyrim.esm", "Unlisted.esm"], masters.Select(master => master.FileName));
    }
}
