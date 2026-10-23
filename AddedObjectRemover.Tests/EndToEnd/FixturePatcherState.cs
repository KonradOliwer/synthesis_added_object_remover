using System.IO.Abstractions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Assets.DI;
using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Synthesis.CLI;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// A Synthesis patcher state over in-memory mods, assembled the way Synthesis assembles it, and the game's plugin list and final
/// world around it: the state's plugins, the patch at the chosen place, then the plugins below the patch.
/// </summary>
internal static class FixturePatcherState
{
    private const string PluginsFileName = "Plugins.txt";

    private const string EnabledMark = "*";

    public static readonly ModKey PatchModKey = ModKey.FromNameAndExtension("AddedObjectRemover.esp");

    /// <summary>The game's plugin list: the state's plugins enabled, the patch, then the plugins below it enabled, read from a file of a fixed name.</summary>
    /// <param name="belowPatch">None when the patch is last.</param>
    public static PluginListRead.Found PluginListOf(
        IPatcherState<ISkyrimMod, ISkyrimModGetter> state, IReadOnlyList<IModKeyed>? belowPatch = null, bool patchEnabled = true) =>
        new(PluginsFileName, [.. ListingsOf(state, belowPatch ?? [], patchEnabled)]);

    /// <summary>Writes the plugins file of <see cref="PluginListOf"/>, with the patch last, into the state's settings folder, for the runs that read it from disk.</summary>
    /// <returns>The path of the plugins file.</returns>
    public static string WritePluginsFile(IPatcherState<ISkyrimMod, ISkyrimModGetter> state) => WritePluginsFile(state, [], patchEnabled: true);

    /// <summary>Writes the plugins file of <see cref="PluginListOf"/> into the state's settings folder, for the runs that read it from disk.</summary>
    /// <returns>The path of the plugins file.</returns>
    public static string WritePluginsFile(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, IReadOnlyList<IModKeyed> belowPatch, bool patchEnabled)
    {
        var path = Path.Combine(state.ExtraSettingsDataPath!.Value.Path, PluginsFileName);
        File.WriteAllLines(path, ListingsOf(state, belowPatch, patchEnabled).Select(listing => (listing.Enabled ? EnabledMark : "") + listing.ModKey.FileName));
        return path;
    }

    /// <summary>The final world of the state, with the given in-memory plugins below the patch.</summary>
    /// <param name="belowPatch">None when the patch is last.</param>
    public static FinalWorld CreateFinalWorld(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, IReadOnlyList<ISkyrimMod>? belowPatch = null)
    {
        var below = belowPatch ?? [];
        return new FinalWorldFactory(OpenInMemory(below)).Create(state.LoadOrder.ListedOrder, state.PatchMod, PluginListOf(state, below), state.DataFolderPath);
    }

    /// <summary>The state of the first patcher of the group, which starts from an empty patch.</summary>
    /// <param name="listedMods">The plugins above the patch, which Synthesis loads.</param>
    public static IPatcherState<ISkyrimMod, ISkyrimModGetter> Create(
        IReadOnlyList<ISkyrimMod> listedMods, string dataFolder, string outputPath) =>
        Create(listedMods, dataFolder, outputPath, new SkyrimMod(PatchModKey, SkyrimRelease.SkyrimSE));

    /// <summary>
    /// The state of a later patcher of the group: as in Synthesis, the output file the patcher before it wrote is imported in full
    /// and writable, under the patch's name, as the patch this patcher adds to.
    /// </summary>
    /// <param name="listedMods">The plugins above the patch, which Synthesis loads.</param>
    /// <param name="earlierPatchFile">The output plugin file of the patcher before it, named like the patch.</param>
    public static IPatcherState<ISkyrimMod, ISkyrimModGetter> CreateAfterEarlierPatcher(
        IReadOnlyList<ISkyrimMod> listedMods, string dataFolder, string outputPath, string earlierPatchFile) =>
        Create(listedMods, dataFolder, outputPath, ImportEarlierPatch(earlierPatchFile));

    /// <remarks>
    /// The patcher's settings folder is the output plugin's folder, so tests find the reports next to the patch.
    /// IPatcherState has internal members, so a test cannot implement it; the obsolete SynthesisState
    /// is the only public implementation. As in Synthesis, the link cache is built before the patch
    /// mod joins the load order as its last listing.
    /// </remarks>
    private static IPatcherState<ISkyrimMod, ISkyrimModGetter> Create(
        IReadOnlyList<ISkyrimMod> listedMods, string dataFolder, string outputPath, SkyrimMod patchMod)
    {
        var loadOrder = new LoadOrder<IModListing<ISkyrimModGetter>>(
            listedMods.Select(mod => new ModListing<ISkyrimModGetter>(mod, enabled: true)));
        var linkCache = loadOrder.ToMutableLinkCache<ISkyrimMod, ISkyrimModGetter>(patchMod);
        loadOrder.Add(new ModListing<ISkyrimModGetter>(patchMod, enabled: true));
        var rawLoadOrder = loadOrder.ListedOrder
            .Select(listing => (ILoadOrderListingGetter)new LoadOrderListing(listing.ModKey, enabled: true))
            .ToList();
        var arguments = new RunSynthesisMutagenPatcher
        {
            OutputPath = outputPath,
            GameRelease = GameRelease.SkyrimSE,
            DataFolderPath = dataFolder,
        };
        var assets = new DataDirectoryAssetProvider(new FileSystem(), new DataDirectoryInjection(dataFolder));
#pragma warning disable CS0618 // SynthesisState is obsolete for patchers, but it is the only public IPatcherState.
        return new SynthesisState<ISkyrimMod, ISkyrimModGetter>(
            arguments, rawLoadOrder, loadOrder, linkCache, assets, patchMod,
            extraDataPath: Path.GetDirectoryName(outputPath), internalDataPath: null, defaultDataPath: null,
            CancellationToken.None, formKeyAllocator: null);
#pragma warning restore CS0618
    }

    /// <summary>
    /// Reads the file with the importer the final world opens plugins from disk with, then copies it into a writable patch, so the
    /// patch holds what a run reading that file through this patcher sees.
    /// </summary>
    private static SkyrimMod ImportEarlierPatch(string earlierPatchFile)
    {
        using var opened = FinalWorldFactory.ImportFromDataFolder(GameRelease.SkyrimSE)(
            Path.GetDirectoryName(earlierPatchFile)!, [new LoadOrderListing(PatchModKey, enabled: true)]);
        return (SkyrimMod)opened.ListedOrder.Single().Mod!.DeepCopy();
    }

    /// <remarks>The state lists the patch last, so the plugins below it follow it.</remarks>
    private static IEnumerable<ILoadOrderListingGetter> ListingsOf(
        IPatcherState<ISkyrimMod, ISkyrimModGetter> state, IReadOnlyList<IModKeyed> belowPatch, bool patchEnabled) =>
        state.LoadOrder.ListedOrder
            .Select(listing => listing.ModKey)
            .Concat(belowPatch.Select(mod => mod.ModKey))
            .Select(key => new LoadOrderListing(key, enabled: key != PatchModKey || patchEnabled));

    /// <summary>Opens the plugins below the patch from memory; every plugin asked for is one of them.</summary>
    private static PluginOpener OpenInMemory(IReadOnlyList<ISkyrimMod> belowPatch) =>
        (_, listings) => new LoadOrder<IModListing<ISkyrimModGetter>>(listings.Select(listing =>
            new ModListing<ISkyrimModGetter>(belowPatch.Single(mod => mod.ModKey == listing.ModKey), listing.Enabled)));
}
