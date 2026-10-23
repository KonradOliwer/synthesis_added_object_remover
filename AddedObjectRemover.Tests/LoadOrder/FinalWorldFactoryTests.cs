using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.LoadOrder;

public sealed class FinalWorldFactoryTests : IDisposable
{
    private const string StalePrefix = "Stale";

    private readonly DirectoryInfo _dataFolder = Directory.CreateTempSubdirectory("aor-final-world-");

    private readonly SkyrimMod _first = NewMod("First.esp");
    private readonly SkyrimMod _second = NewMod("Second.esp");
    private readonly SkyrimMod _patch = NewMod("Patch.esp");
    private readonly SkyrimMod _below = NewMod("Below.esp");
    private readonly SkyrimMod _lowest = NewMod("Lowest.esp");

    public void Dispose() => _dataFolder.Delete(recursive: true);

    [Fact]
    public void ThePluginsAboveThePatchAreTheStatesListingsThemselves()
    {
        var state = StateOf(_first, _second);

        using var world = Create(state, OpenInMemory(_below), Entry(_first), Entry(_second), Entry(_patch), Entry(_below));

        Assert.Same(state[0], world.Order.ListedOrder.ElementAt(0));
        Assert.Same(state[1], world.Order.ListedOrder.ElementAt(1));
    }

    [Fact]
    public void ThePatchSitsAtItsEntryAndTheEnabledPluginsBelowItFollowInListOrder()
    {
        using var world = Create(
            StateOf(_first, _second), OpenInMemory(_below, _lowest), Entry(_first), Entry(_second), Entry(_patch), Entry(_below), Entry(_lowest));

        Assert.Equal([_first.ModKey, _second.ModKey, _patch.ModKey, _below.ModKey, _lowest.ModKey], KeysOf(world));
        Assert.Same(_patch, world.Order.ListedOrder.ElementAt(2).Mod);
        Assert.Same(_below, world.Order.ListedOrder.ElementAt(3).Mod);
        Assert.Equal(2, world.Plugins.PatchPosition);
    }

    [Fact]
    public void WithoutAnEntryThePatchIsLastAndNothingIsOpened()
    {
        var asked = new List<ModKey>();

        using var world = Create(StateOf(_first, _second), Recording(asked, OpenInMemory()), Entry(_first), Entry(_second));

        Assert.Equal([_first.ModKey, _second.ModKey, _patch.ModKey], KeysOf(world));
        Assert.Empty(asked);
        Assert.Equal(2, world.Plugins.PatchPosition);
    }

    [Fact]
    public void DisabledEntriesBelowThePatchAreNotOpened()
    {
        var asked = new List<ModKey>();

        using var world = Create(
            StateOf(_first), Recording(asked, OpenInMemory(_below, _lowest)), Entry(_first), Entry(_patch), Entry(_below, enabled: false), Entry(_lowest));

        Assert.Equal([_lowest.ModKey], asked);
        Assert.Equal([_first.ModKey, _patch.ModKey, _lowest.ModKey], KeysOf(world));
        Assert.Contains(new PluginName("Below.esp"), world.Plugins.DisabledPlugins);
    }

    [Fact]
    public void AnEntryBelowThePatchWithoutAFileIsLeftOutAndNamed()
    {
        using var world = Create(StateOf(_first), OpenInMemory(_lowest), Entry(_first), Entry(_patch), Entry(_below), Entry(_lowest));

        Assert.Equal([_first.ModKey, _patch.ModKey, _lowest.ModKey], KeysOf(world));
        Assert.Equal(new PluginName("Below.esp"), Assert.Single(world.Plugins.MissingPlugins));
    }

    [Fact]
    public void ThePluginsTableIsBuiltFromTheComposedOrder()
    {
        using var world = Create(StateOf(_first), OpenInMemory(_below), Entry(_first), Entry(_patch), Entry(_below));

        Assert.Equal(
            [new PluginName("First.esp"), new PluginName("Patch.esp"), new PluginName("Below.esp")],
            world.Plugins.Listed.Select(listing => listing.Mod));
        Assert.Equal(2, world.Plugins.Positions.Of(new PluginName("Below.esp")));
    }

    [Fact]
    public void APluginBothAboveAndBelowThePatchIsABug()
    {
        var factory = new FinalWorldFactory(OpenInMemory(_first));

        Assert.Throws<ArgumentException>(() =>
            factory.Create(StateOf(_first), _patch, ListOf(Entry(_first), Entry(_patch), Entry(_first)), _dataFolder.FullName));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnOldCopyOfThePatchOnDiskContributesNoRecord(bool enabled)
    {
        var firstStatic = _first.Statics.AddNew("FirstStatic");
        WriteStaleCopyOfThePatch(firstStatic);
        _below.Statics.AddNew("BelowStatic");
        _below.WriteToBinary(PathOf(_below));

        using var world = Create(
            StateOf(_first), FinalWorldFactory.ImportFromDataFolder(GameRelease.SkyrimSE), Entry(_first), Entry(_patch, enabled), Entry(_below));

        Assert.Equal([_first.ModKey, _patch.ModKey, _below.ModKey], KeysOf(world));
        Assert.Same(_patch, world.Order.ListedOrder.ElementAt(1).Mod);
        var editorIds = world.Order.ListedOrder
            .SelectMany(listing => listing.Mod!.EnumerateMajorRecords())
            .Select(record => record.EditorID)
            .ToList();
        Assert.Contains("BelowStatic", editorIds);
        Assert.DoesNotContain(editorIds, editorId => editorId!.StartsWith(StalePrefix, StringComparison.Ordinal));
    }

    [Fact]
    public void DisposingClosesOnlyThePluginsTheFactoryOpened()
    {
        _first.Statics.AddNew("FirstStatic");
        _first.WriteToBinary(PathOf(_first));
        _below.Statics.AddNew("BelowStatic");
        _below.WriteToBinary(PathOf(_below));
        using var firstFromDisk = SkyrimMod.CreateFromBinaryOverlay(PathOf(_first), SkyrimRelease.SkyrimSE);

        var world = Create(
            [new ModListing<ISkyrimModGetter>(firstFromDisk, enabled: true)],
            FinalWorldFactory.ImportFromDataFolder(GameRelease.SkyrimSE),
            Entry(_first), Entry(_patch), Entry(_below));
        var belowFromDisk = world.Order.ListedOrder.ElementAt(2).Mod!;
        Assert.Equal(1, belowFromDisk.Statics.Count);
        world.Dispose();

        Assert.Equal(1, firstFromDisk.Statics.Count);
        Assert.ThrowsAny<Exception>(() => belowFromDisk.Statics.Count);
    }

    /// <summary>The stale copy holds a record of its own and an override of a plugin above the patch, both marked by their editor IDs.</summary>
    private void WriteStaleCopyOfThePatch(IStaticGetter aboveRecord)
    {
        var stale = new SkyrimMod(_patch.ModKey, SkyrimRelease.SkyrimSE);
        stale.Statics.AddNew(StalePrefix + "New");
        var overridden = (Static)aboveRecord.DeepCopy();
        overridden.EditorID = StalePrefix + "Override";
        stale.Statics.Add(overridden);
        stale.WriteToBinary(PathOf(stale));
    }

    private FinalWorld Create(IReadOnlyList<IModListing<ISkyrimModGetter>> state, PluginOpener opener, params ILoadOrderListingGetter[] entries) =>
        new FinalWorldFactory(opener).Create(state, _patch, ListOf(entries), _dataFolder.FullName);

    /// <summary>The Synthesis state's listings: the given plugins, then the patch last, as Synthesis appends it.</summary>
    private List<IModListing<ISkyrimModGetter>> StateOf(params SkyrimMod[] above) =>
        [.. above.Append(_patch).Select(mod => new ModListing<ISkyrimModGetter>(mod, enabled: true))];

    private string PathOf(IModKeyed mod) => Path.Combine(_dataFolder.FullName, mod.ModKey.FileName);

    private static PluginListRead.Found ListOf(params ILoadOrderListingGetter[] entries) => new("Plugins.txt", [.. entries]);

    private static ILoadOrderListingGetter Entry(IModKeyed mod, bool enabled = true) => new LoadOrderListing(mod.ModKey, enabled);

    private static List<ModKey> KeysOf(FinalWorld world) => [.. world.Order.ListedOrder.Select(listing => listing.ModKey)];

    private static SkyrimMod NewMod(string fileName) => new(ModKey.FromNameAndExtension(fileName), SkyrimRelease.SkyrimSE);

    /// <summary>Opens the given in-memory plugins by name; any other plugin has no file.</summary>
    private static PluginOpener OpenInMemory(params SkyrimMod[] files) =>
        (_, listings) => new LoadOrder<IModListing<ISkyrimModGetter>>(listings.Select(listing =>
            files.FirstOrDefault(file => file.ModKey == listing.ModKey) is { } file
                ? new ModListing<ISkyrimModGetter>(file, listing.Enabled)
                : ModListing<ISkyrimModGetter>.CreateUnloaded(listing.ModKey, listing.Enabled)));

    private static PluginOpener Recording(List<ModKey> asked, PluginOpener opener) =>
        (dataFolder, listings) =>
        {
            asked.AddRange(listings.Select(listing => listing.ModKey));
            return opener(dataFolder, listings);
        };
}
