using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.LoadOrder;

public sealed class EnabledPluginListReaderTests : IDisposable
{
    private static readonly ModKey Skyrim = ModKey.FromNameAndExtension("Skyrim.esm");
    private static readonly ModKey Patch = ModKey.FromNameAndExtension("Patch.esp");
    private static readonly ModKey First = ModKey.FromNameAndExtension("First.esp");
    private static readonly ModKey Second = ModKey.FromNameAndExtension("Second.esp");

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("aor-plugin-list-");

    public void Dispose() => _folder.Delete(recursive: true);

    [Fact]
    public void EachEntryKeepsItsPlaceAndItsEnabledFlag()
    {
        var path = WritePluginsFile("*Skyrim.esm", "*First.esp", "Second.esp", "*Patch.esp");

        var found = Assert.IsType<PluginListRead.Found>(Read(path, [Skyrim, First, Second]));

        Assert.Equal(path, found.FilePath);
        Assert.Equal(
            [(Skyrim, true), (First, true), (Second, false), (Patch, true)],
            found.Listings.Select(listing => (listing.ModKey, listing.Enabled)));
    }

    [Fact]
    public void TheStateMayLackAnEntryForThePatch()
    {
        var path = WritePluginsFile("*Skyrim.esm", "*First.esp");

        var found = Assert.IsType<PluginListRead.Found>(Read(path, [Skyrim, First, Patch]));

        Assert.Equal([Skyrim, First], found.Listings.Select(listing => listing.ModKey));
    }

    [Fact]
    public void AMissingFileIsAProblemWithItsPath()
    {
        var path = Path.Combine(_folder.FullName, "Missing.txt");

        var failed = Assert.IsType<PluginListRead.Failed>(Read(path, []));

        Assert.Equal(new PluginListProblem.FileMissing(path), failed.Problem);
    }

    [Fact]
    public void AMalformedLineIsAProblemWithItsNumberCountedFromOne()
    {
        var path = WritePluginsFile("*Skyrim.esm", "", "# a comment", "NoExtension", "*First.esp");

        var failed = Assert.IsType<PluginListRead.Failed>(Read(path, [Skyrim]));

        Assert.Equal(new PluginListProblem.MalformedLine(path, 4), failed.Problem);
    }

    [Fact]
    public void APluginOfTheStateThatTheListLacksIsAProblemWithItsName()
    {
        var path = WritePluginsFile("*Skyrim.esm", "*First.esp");

        var failed = Assert.IsType<PluginListRead.Failed>(Read(path, [Skyrim, First, Second]));

        Assert.Equal(new PluginListProblem.PluginNotListed(path, new PluginName("Second.esp")), failed.Problem);
    }

    [Fact]
    public void ADisabledEntryCountsAsListed()
    {
        var path = WritePluginsFile("*Skyrim.esm", "First.esp");

        Assert.IsType<PluginListRead.Found>(Read(path, [Skyrim, First]));
    }

    private PluginListRead Read(string path, IReadOnlyList<ModKey> statePlugins) =>
        EnabledPluginListReader.Read(GameRelease.SkyrimSE, _folder.FullName, path, statePlugins, Patch);

    private string WritePluginsFile(params string[] lines)
    {
        var path = Path.Combine(_folder.FullName, "Plugins.txt");
        File.WriteAllLines(path, lines);
        return path;
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalAppDataCollection
{
    public const string Name = "LocalAppData";
}

/// <summary>The default location comes from an environment variable, so these tests change it and run alone.</summary>
[Collection(LocalAppDataCollection.Name)]
public sealed class EnabledPluginListDefaultLocationTests
{
    private const string LocalAppData = "LocalAppData";

    [Fact]
    public void AnUnknownDefaultLocationIsAProblem()
    {
        var original = Environment.GetEnvironmentVariable(LocalAppData);
        Environment.SetEnvironmentVariable(LocalAppData, null);
        try
        {
            var failed = Assert.IsType<PluginListRead.Failed>(
                EnabledPluginListReader.Read(GameRelease.SkyrimSE, Path.GetTempPath(), pluginsFile: null, [], ModKey.FromNameAndExtension("Patch.esp")));

            Assert.Equal(new PluginListProblem.PathUnknown(), failed.Problem);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LocalAppData, original);
        }
    }
}
