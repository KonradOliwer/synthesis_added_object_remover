using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.LoadOrder;

public class CompatibilityPatchTests
{
    private static readonly ModKey Skyrim = ModKey.FromNameAndExtension("Skyrim.esm");
    private static readonly ModKey TargetMaster = ModKey.FromNameAndExtension("TargetMaster.esm");
    private static readonly ModKey Target = ModKey.FromNameAndExtension("Target.esp");
    private static readonly ModKey OtherA = ModKey.FromNameAndExtension("OtherA.esp");
    private static readonly ModKey OtherB = ModKey.FromNameAndExtension("OtherB.esp");
    private static readonly ModKey OtherC = ModKey.FromNameAndExtension("OtherC.esp");
    private static readonly ModKey Patch = ModKey.FromNameAndExtension("Target - OtherA Patch.esp");
    private static readonly ModKey Addon = ModKey.FromNameAndExtension("Target Addon.esp");
    private static readonly ModKey Merged = ModKey.FromNameAndExtension("Merged.esp");
    private static readonly ModKey Unrelated = ModKey.FromNameAndExtension("Unrelated.esp");
    private static readonly ModKey Missing = ModKey.FromNameAndExtension("Missing.esp");
    private static readonly ModKey SynthesisOutput = ModKey.FromNameAndExtension("Synthesis.esp");

    [Fact]
    public void PluginMasteringTargetAndFewOtherModsIsAPatch()
    {
        var loadOrder = new List<IModListingGetter<ISkyrimModGetter>>
        {
            Listing(Skyrim),
            Listing(TargetMaster, Skyrim),
            Listing(Target, Skyrim, TargetMaster),
            Listing(OtherA, Skyrim),
            Listing(Patch, Skyrim, Target, OtherA),
            Listing(Addon, Skyrim, TargetMaster, Target),
            Listing(Merged, Target, OtherA, OtherB, OtherC),
            Listing(Unrelated, OtherA, OtherB),
            new ModListing<ISkyrimModGetter>(Missing, mod: null, enabled: true),
        };

        var result = Find(loadOrder);

        Assert.Equal(3, result.PluginsMasteringTarget);
        var patch = Assert.Single(result.Patches);
        Assert.Equal(Patch, patch.Patch);
        Assert.Equal(new[] { OtherA }, patch.OtherMasters);
        var skipped = Assert.Single(result.SkippedTooManyMasters);
        Assert.Equal(Merged, skipped.Patch);
        Assert.Equal(new[] { Patch, OtherA }.ToHashSet(), result.CollectIgnoredMods());
    }

    [Fact]
    public void OutputOfEarlierPatchersIsNeverAPatch()
    {
        var loadOrder = new List<IModListingGetter<ISkyrimModGetter>>
        {
            Listing(Skyrim),
            Listing(Target, Skyrim),
            Listing(OtherA, Skyrim),
            Listing(SynthesisOutput, Skyrim, Target, OtherA),
        };

        var result = Find(loadOrder);

        Assert.Equal(0, result.PluginsMasteringTarget);
        Assert.Empty(result.Patches);
        Assert.Empty(result.CollectIgnoredMods());
    }

    private static CompatibilityPatches Find(IEnumerable<IModListingGetter<ISkyrimModGetter>> loadOrder) =>
        CompatibilityPatchDetector.Find(
            loadOrder, Target, SynthesisOutput, new HashSet<ModKey> { TargetMaster }, new HashSet<ModKey> { Skyrim }, maxOtherMasters: 2);

    private static ModListing<ISkyrimModGetter> Listing(ModKey key, params ModKey[] masters)
    {
        var mod = new SkyrimMod(key, SkyrimRelease.SkyrimSE);
        foreach (var master in masters) mod.ModHeader.MasterReferences.Add(new MasterReference { Master = master });
        return new ModListing<ISkyrimModGetter>(mod);
    }
}
