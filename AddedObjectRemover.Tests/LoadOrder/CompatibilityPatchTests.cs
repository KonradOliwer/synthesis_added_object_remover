using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.LoadOrder;

public class CompatibilityPatchTests
{
    private const int PatchMasterLimit = 2;

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

    private static readonly ModTable Mods = new(
        [Skyrim, TargetMaster, Target, OtherA, OtherB, OtherC, Patch, Addon, Merged, Unrelated, Missing, SynthesisOutput]);

    [Fact]
    public void PluginMasteringTargetAndFewOtherModsIsAPatch()
    {
        var standing = Decide(
            Listing(Skyrim),
            Listing(TargetMaster, Skyrim),
            Listing(Target, Skyrim, TargetMaster),
            Listing(OtherA, Skyrim),
            Listing(Patch, Skyrim, Target, OtherA),
            Listing(Addon, Skyrim, TargetMaster, Target),
            Listing(Merged, Target, OtherA, OtherB, OtherC),
            Listing(Unrelated, OtherA, OtherB),
            new ModListing(Mods.RefOf(Missing), Loaded: false, []));

        var result = standing.Patches;

        Assert.Equal(3, result.PluginsMasteringTarget);
        var patch = Assert.Single(result.Detected);
        Assert.Equal(Patch, Mods.KeyOf(patch.Patch));
        Assert.Equal(new[] { OtherA }, patch.OtherMasters.Select(Mods.KeyOf));
        var skipped = Assert.Single(result.SkippedTooManyMasters);
        Assert.Equal(Merged, Mods.KeyOf(skipped.Patch));
        Assert.Equal(new[] { Patch, OtherA }.ToHashSet(), result.IgnoredMods.Select(Mods.KeyOf).ToHashSet());
    }

    [Fact]
    public void OutputOfEarlierPatchersIsNeverAPatch()
    {
        var standing = Decide(
            Listing(Skyrim),
            Listing(Target, Skyrim),
            Listing(OtherA, Skyrim),
            Listing(SynthesisOutput, Skyrim, Target, OtherA));

        var result = standing.Patches;

        Assert.Equal(0, result.PluginsMasteringTarget);
        Assert.Empty(result.Detected);
        Assert.Empty(result.IgnoredMods);
    }

    private static ModStanding Decide(params ModListing[] listed) =>
        Standing.Decide(
            new ModFacts(Mods, Mods.RefOf(SynthesisOutput), [.. listed]),
            new StandingOptions(Mods.RefOf(Target), Excluded: [], IgnoreTargetMasters: true, IgnorePatched: true, PatchMasterLimit));

    private static ModListing Listing(ModKey key, params ModKey[] masters) =>
        new(Mods.RefOf(key), Loaded: true, [.. masters.Select(Mods.RefOf)]);
}
