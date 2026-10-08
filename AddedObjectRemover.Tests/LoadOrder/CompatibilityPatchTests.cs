namespace AddedObjectRemover.Tests.LoadOrder;

public class CompatibilityPatchTests
{
    private const int PatchMasterLimit = 2;

    private static readonly PluginName Skyrim = new("Skyrim.esm");
    private static readonly PluginName TargetMaster = new("TargetMaster.esm");
    private static readonly PluginName Target = new("Target.esp");
    private static readonly PluginName OtherA = new("OtherA.esp");
    private static readonly PluginName OtherB = new("OtherB.esp");
    private static readonly PluginName OtherC = new("OtherC.esp");
    private static readonly PluginName Patch = new("Target - OtherA Patch.esp");
    private static readonly PluginName Addon = new("Target Addon.esp");
    private static readonly PluginName Merged = new("Merged.esp");
    private static readonly PluginName Unrelated = new("Unrelated.esp");
    private static readonly PluginName Missing = new("Missing.esp");
    private static readonly PluginName SynthesisOutput = new("Synthesis.esp");

    private static readonly KnownPluginNames Mods = new(
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
            new PluginListing(Missing, Loaded: false, []));

        var result = standing.Patches;

        Assert.Equal(3, result.PluginsMasteringTarget);
        var patch = Assert.Single(result.Detected);
        Assert.Equal(Patch, patch.Patch);
        Assert.Equal(new[] { OtherA }, patch.OtherMasters);
        var skipped = Assert.Single(result.SkippedTooManyMasters);
        Assert.Equal(Merged, skipped.Patch);
        Assert.Equal(new[] { Patch, OtherA }.ToHashSet(), result.IgnoredMods.ToHashSet());
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

    private static IdentifiedMods Decide(params PluginListing[] listed) =>
        IdentifyTheMods.Decide(
            new LoadOrderPlugins(Mods, SynthesisOutput, [.. listed]),
            new ModIdentificationSettings(Target, Excluded: [], IgnoreTargetMasters: true, IgnorePatched: true, PatchMasterLimit));

    private static PluginListing Listing(PluginName plugin, params PluginName[] masters) =>
        new(plugin, Loaded: true, [.. masters]);
}
