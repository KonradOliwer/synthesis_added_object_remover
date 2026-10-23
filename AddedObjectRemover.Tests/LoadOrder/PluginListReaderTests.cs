using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.LoadOrder;

public sealed class PluginListReaderTests : IDisposable
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
    public void ThePluginListMayLackAnEntryForThePatch()
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

    /// <summary>Synthesis loads only the plugins above the patch's entry, so the list changed after Synthesis read it.</summary>
    [Fact]
    public void APluginOfTheStateListedAfterThePatchIsAProblemWithItsNameAndThePatch()
    {
        var path = WritePluginsFile("*Skyrim.esm", "*Patch.esp", "*First.esp");

        var failed = Assert.IsType<PluginListRead.Failed>(Read(path, [Skyrim, First, Patch]));

        Assert.Equal(new PluginListProblem.PluginListedAfterPatch(path, new PluginName("First.esp"), new PluginName("Patch.esp")), failed.Problem);
    }

    [Fact]
    public void APluginNotOfTheStateMayBeListedAfterThePatch()
    {
        var path = WritePluginsFile("*Skyrim.esm", "*First.esp", "*Patch.esp", "*Second.esp");

        Assert.IsType<PluginListRead.Found>(Read(path, [Skyrim, First, Patch]));
    }

    [Fact]
    public void ADisabledEntryCountsAsListed()
    {
        var path = WritePluginsFile("*Skyrim.esm", "First.esp");

        Assert.IsType<PluginListRead.Found>(Read(path, [Skyrim, First]));
    }

    private PluginListRead Read(string path, IReadOnlyList<ModKey> statePlugins) =>
        PluginListReader.Read(GameRelease.SkyrimSE, _folder.FullName, path, statePlugins, Patch);

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

/// <summary>
/// The default location comes from an environment variable, so these tests change it and run alone. They read the list the way
/// the patcher does in Synthesis, with no plugins file given.
/// </summary>
[Collection(LocalAppDataCollection.Name)]
public sealed class PluginListDefaultLocationTests : IDisposable
{
    private const string LocalAppData = "LocalAppData";

    /// <summary>The folder the game keeps its plugin list in, under the local application data folder.</summary>
    private const string GameFolderName = "Skyrim Special Edition";

    private const string PluginsFileName = "Plugins.txt";

    private static readonly ModKey Patch = ModKey.FromNameAndExtension("Patch.esp");

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("aor-plugin-list-default-");

    public void Dispose() => _folder.Delete(recursive: true);

    [Fact]
    public void AnUnknownDefaultLocationIsAProblem()
    {
        var failed = Assert.IsType<PluginListRead.Failed>(WithLocalAppData(null, () => ReadDefault(Path.GetTempPath())));

        Assert.Equal(new PluginListProblem.PathUnknown(), failed.Problem);
    }

    [Fact]
    public void TheDefaultPluginsFileIsReadAndNamedInTheResult()
    {
        var path = WriteDefaultPluginsFile("*Skyrim.esm", "*First.esp", "Second.esp");
        var dataFolder = Directory.CreateDirectory(Path.Combine(_folder.FullName, "Data")).FullName;

        var found = Assert.IsType<PluginListRead.Found>(WithLocalAppData(_folder.FullName, () => ReadDefault(dataFolder)));

        Assert.Equal(path, found.FilePath);
        Assert.Equal(
            [("Skyrim.esm", true), ("First.esp", true), ("Second.esp", false)],
            found.Listings.Select(listing => (listing.ModKey.FileName.String, listing.Enabled)));
    }

    [Fact]
    public void AMissingDefaultPluginsFileIsAProblemWithThePathTheReadWouldUse()
    {
        var expectedPath = Path.Combine(_folder.FullName, GameFolderName, PluginsFileName);

        var failed = Assert.IsType<PluginListRead.Failed>(WithLocalAppData(_folder.FullName, () => ReadDefault(_folder.FullName)));

        Assert.Equal(new PluginListProblem.FileMissing(expectedPath), failed.Problem);
    }

    private static PluginListRead ReadDefault(string dataFolder) =>
        PluginListReader.Read(GameRelease.SkyrimSE, dataFolder, pluginsFile: null, [], Patch);

    private string WriteDefaultPluginsFile(params string[] lines)
    {
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(_folder.FullName, GameFolderName)).FullName, PluginsFileName);
        File.WriteAllLines(path, lines);
        return path;
    }

    private static PluginListRead WithLocalAppData(string? folder, Func<PluginListRead> read)
    {
        var original = Environment.GetEnvironmentVariable(LocalAppData);
        Environment.SetEnvironmentVariable(LocalAppData, folder);
        try
        {
            return read();
        }
        finally
        {
            Environment.SetEnvironmentVariable(LocalAppData, original);
        }
    }
}
