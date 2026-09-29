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

/// <summary>A Synthesis patcher state over in-memory mods, assembled the way Synthesis assembles it.</summary>
internal static class FixturePatcherState
{
    public static readonly ModKey PatchModKey = ModKey.FromNameAndExtension("AddedObjectRemover.esp");

    /// <remarks>
    /// IPatcherState has internal members, so a test cannot implement it; the obsolete SynthesisState
    /// is the only public implementation. As in Synthesis, the link cache is built before the patch
    /// mod joins the load order as its last listing.
    /// </remarks>
    public static IPatcherState<ISkyrimMod, ISkyrimModGetter> Create(
        IReadOnlyList<ISkyrimMod> listedMods, string dataFolder, string outputPath)
    {
        var patchMod = new SkyrimMod(PatchModKey, SkyrimRelease.SkyrimSE);
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
            extraDataPath: null, internalDataPath: null, defaultDataPath: null,
            CancellationToken.None, formKeyAllocator: null);
#pragma warning restore CS0618
    }
}
