using AddedObjectRemover.Tests.Fixtures;

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

    [Fact]
    public void RequiringModsAreTheLoadedPluginsMasteringTheTargetInLoadOrderWithoutThePatchPlugin()
    {
        var standing = Decide(
            Listing(Skyrim),
            Listing(Target, Skyrim),
            Listing(Patch, Skyrim, Target, OtherA),
            Listing(OtherA, Skyrim),
            Listing(Addon, Skyrim, Target),
            Listing(Unrelated, OtherA),
            new PluginListing(Missing, Loaded: false, [Target]),
            Listing(SynthesisOutput, Skyrim, Target));

        Assert.Equal(new[] { Patch, Addon }, standing.RequiringMods);
    }

    [Fact]
    public void RequiringModsAreListedWhenPatchDetectionIsOff()
    {
        var standing = ModIdentifier.Identify(
            TestLoadOrderPlugins.Of(Mods, SynthesisOutput, [Listing(Skyrim), Listing(Target, Skyrim), Listing(Addon, Skyrim, Target)]),
            new ModIdentificationSettings(Target, Excluded: [], IgnoreTargetMasters: true, IgnorePatched: false, PatchMasterLimit));

        Assert.Equal(new[] { Addon }, standing.RequiringMods);
        Assert.False(standing.Patches.DetectionOn);
    }

    [Fact]
    public void TheBaseGameIsIgnoredWithoutCreationClubContent()
    {
        var dawnguard = new PluginName("Dawnguard.esm");
        var creationClub = new PluginName("ccBGSSSE001-Fish.esm");
        var standing = ModIdentifier.Identify(
            TestLoadOrderPlugins.Of(new KnownPluginNames([Skyrim, dawnguard, creationClub, Target, SynthesisOutput]), SynthesisOutput,
                [Listing(Skyrim), Listing(dawnguard, Skyrim), Listing(creationClub, Skyrim), Listing(Target, Skyrim)]),
            new ModIdentificationSettings(Target, Excluded: [], IgnoreTargetMasters: false, IgnorePatched: true, PatchMasterLimit));

        Assert.Contains(Skyrim, standing.IgnoredOrigins);
        Assert.Contains(dawnguard, standing.IgnoredOrigins);
        Assert.DoesNotContain(creationClub, standing.IgnoredOrigins);
    }

    private static IdentifiedMods Decide(params PluginListing[] listed) =>
        ModIdentifier.Identify(
            TestLoadOrderPlugins.Of(Mods, SynthesisOutput, [.. listed]),
            new ModIdentificationSettings(Target, Excluded: [], IgnoreTargetMasters: true, IgnorePatched: true, PatchMasterLimit));

    private static PluginListing Listing(PluginName plugin, params PluginName[] masters) =>
        new(plugin, Loaded: true, [.. masters]);
}
