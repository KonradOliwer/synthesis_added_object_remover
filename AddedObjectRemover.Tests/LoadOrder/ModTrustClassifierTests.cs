using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

public class ModTrustClassifierTests
{
    private const int MaxOtherMasters = 2;

    private static readonly PluginName Skyrim = new("Skyrim.esm");
    private static readonly PluginName Dawnguard = new("Dawnguard.esm");
    private static readonly PluginName CreationClub = new("ccBGSSSE001-Fish.esm");
    private static readonly PluginName CleanedModMaster = new("CleanedModMaster.esm");
    private static readonly PluginName CleanedMod = new("CleanedMod.esp");
    private static readonly PluginName CityModMaster = new("CityModMaster.esm");
    private static readonly PluginName CityMod = new("CityMod.esp");
    private static readonly PluginName Birds = new("Birds.esp");
    private static readonly PluginName Addon = new("CleanedMod Addon.esp");
    private static readonly PluginName CityPatch = new("CityMod - CleanedMod Patch.esp");
    private static readonly PluginName Merged = new("Merged.esp");
    private static readonly PluginName OtherA = new("OtherA.esp");
    private static readonly PluginName OtherB = new("OtherB.esp");
    private static readonly PluginName OtherC = new("OtherC.esp");
    private static readonly PluginName OtherGroupPatch = new("Other Group Patch.esp");
    private static readonly PluginName NotListed = new("NotListed.esm");
    private static readonly PluginName Patch = new("Synthesis.esp");

    /// <summary>The base game, the cleaned mod's master and the cleaned mod, which every load order below starts with.</summary>
    private static readonly PluginListing[] CleanedModAndMasters =
    [
        Listing(Skyrim),
        Listing(CleanedModMaster, Skyrim),
        Listing(CleanedMod, Skyrim, CleanedModMaster),
    ];

    [Fact]
    public void TheBaseGameTheCleanedModAndItsMastersAreAlwaysTrusted()
    {
        var table = ClassifyInOrder(
            Settings(),
            Listing(Skyrim),
            Listing(Dawnguard, Skyrim),
            Listing(CreationClub, Skyrim),
            Listing(CleanedModMaster, Skyrim),
            Listing(CleanedMod, Skyrim, CleanedModMaster),
            Listing(CityMod, Skyrim));

        AssertTrust(table, Skyrim, ModTrust.Trusted, TrustReason.BaseGame);
        AssertTrust(table, Dawnguard, ModTrust.Trusted, TrustReason.BaseGame);
        AssertTrust(table, CleanedMod, ModTrust.Trusted, TrustReason.CleanedMod);
        AssertTrust(table, CleanedModMaster, ModTrust.Trusted, TrustReason.CleanedModMaster);
        AssertTrust(table, CreationClub, ModTrust.Untrusted, null);
        AssertTrust(table, CityMod, ModTrust.Untrusted, null);
    }

    [Fact]
    public void ListedPluginsAndTheirMastersAreTrustedAlsoMastersThatAreNotListed()
    {
        var table = Classify(
            Settings(trusted: [CityMod]),
            Listing(CityModMaster, Skyrim),
            Listing(CityMod, Skyrim, CityModMaster, NotListed));

        AssertTrust(table, CityMod, ModTrust.Trusted, TrustReason.Listed);
        AssertTrust(table, CityModMaster, ModTrust.Trusted, TrustReason.MasterOfListed);
        AssertTrust(table, NotListed, ModTrust.Trusted, TrustReason.MasterOfListed);
        AssertTrust(table, Skyrim, ModTrust.Trusted, TrustReason.BaseGame);
    }

    [Fact]
    public void PluginsListedForOwnChangesOnlyAreTrustedForOwnChangesAndTheirMastersAreNot()
    {
        var table = Classify(
            Settings(ownChanges: [Birds]),
            Listing(CityMod, Skyrim),
            Listing(Birds, Skyrim, CityMod));

        AssertTrust(table, Birds, ModTrust.TrustedForOwnChanges, TrustReason.ListedOwnChangesOnly);
        AssertTrust(table, CityMod, ModTrust.Untrusted, null);
    }

    [Fact]
    public void NeverTrustedPluginsAreUntrusted()
    {
        var table = Classify(Settings(never: [CityMod]), Listing(CityMod, Skyrim));

        AssertTrust(table, CityMod, ModTrust.Untrusted, TrustReason.NeverTrusted);
    }

    [Fact]
    public void NeverTrustedBeatsEveryOtherRule()
    {
        var table = ClassifyInOrder(
            Settings(trusted: [CityMod], ownChanges: [Birds], never: [Skyrim, CleanedModMaster, CityModMaster, CityMod, Birds, Addon, CreationClub], trustLoadingBefore: true),
            Listing(Skyrim),
            Listing(CreationClub, Skyrim),
            Listing(CleanedModMaster, Skyrim),
            Listing(CleanedMod, Skyrim, CleanedModMaster),
            Listing(CityModMaster, Skyrim),
            Listing(CityMod, Skyrim, CityModMaster),
            Listing(Birds, Skyrim),
            Listing(Addon, Skyrim, CleanedMod));

        foreach (var plugin in new[] { Skyrim, CreationClub, CleanedModMaster, CityModMaster, CityMod, Birds, Addon })
        {
            AssertTrust(table, plugin, ModTrust.Untrusted, TrustReason.NeverTrusted);
        }
    }

    [Fact]
    public void TheMastersOfANeverTrustedPluginAreNotTrustedThroughItsOtherListing()
    {
        var table = Classify(
            Settings(trusted: [CityMod], never: [CityMod]),
            Listing(CityModMaster, Skyrim),
            Listing(CityMod, Skyrim, CityModMaster));

        AssertTrust(table, CityMod, ModTrust.Untrusted, TrustReason.NeverTrusted);
        AssertTrust(table, CityModMaster, ModTrust.Untrusted, null);
    }

    [Fact]
    public void ListedForOwnChangesOnlyBeatsTheAutomaticRules()
    {
        var table = ClassifyInOrder(
            Settings(ownChanges: [CreationClub, CityPatch], trustLoadingBefore: true),
            Listing(Skyrim),
            Listing(CreationClub, Skyrim),
            Listing(CleanedMod, Skyrim),
            Listing(CityMod, Skyrim),
            Listing(CityPatch, Skyrim, CleanedMod, CityMod));

        AssertTrust(table, CreationClub, ModTrust.TrustedForOwnChanges, TrustReason.ListedOwnChangesOnly);
        AssertTrust(table, CityPatch, ModTrust.TrustedForOwnChanges, TrustReason.ListedOwnChangesOnly);
        AssertTrust(table, CityMod, ModTrust.Untrusted, null);
    }

    [Fact]
    public void ListedWithMastersBeatsOwnChangesOnlyForAMaster()
    {
        var table = Classify(
            Settings(trusted: [CityMod], ownChanges: [CityModMaster, CityMod]),
            Listing(CityModMaster, Skyrim),
            Listing(CityMod, Skyrim, CityModMaster));

        AssertTrust(table, CityMod, ModTrust.Trusted, TrustReason.Listed);
        AssertTrust(table, CityModMaster, ModTrust.Trusted, TrustReason.MasterOfListed);
    }

    [Fact]
    public void AModRequiringTheCleanedModWithNoOtherMastersIsTrusted()
    {
        var table = Classify(Settings(), Listing(Addon, Skyrim, Dawnguard, CleanedModMaster, CleanedMod));

        AssertTrust(table, Addon, ModTrust.Trusted, TrustReason.RequiringMod);
        Assert.Equal(0, table.Plugins[Addon].OtherMasterCount);
    }

    [Fact]
    public void AModRequiringTheCleanedModAtTheLimitIsTrustedWithItsMasters()
    {
        var table = Classify(
            Settings(),
            Listing(OtherA, Skyrim),
            Listing(OtherB, Skyrim),
            Listing(CityPatch, Skyrim, CleanedModMaster, CleanedMod, OtherA, OtherB));

        AssertTrust(table, CityPatch, ModTrust.Trusted, TrustReason.RequiringMod);
        AssertTrust(table, OtherA, ModTrust.Trusted, TrustReason.MasterOfRequiringMod);
        AssertTrust(table, OtherB, ModTrust.Trusted, TrustReason.MasterOfRequiringMod);
        Assert.Equal(MaxOtherMasters, table.Plugins[CityPatch].OtherMasterCount);
    }

    [Fact]
    public void AModRequiringTheCleanedModAboveTheLimitIsUntrustedAndSoAreItsMasters()
    {
        var table = Classify(
            Settings(),
            Listing(OtherA, Skyrim),
            Listing(OtherB, Skyrim),
            Listing(OtherC, Skyrim),
            Listing(Merged, Skyrim, CleanedMod, OtherA, OtherB, OtherC));

        AssertTrust(table, Merged, ModTrust.Untrusted, TrustReason.RequiringModTooManyMasters);
        Assert.Equal(MaxOtherMasters + 1, table.Plugins[Merged].OtherMasterCount);
        foreach (var master in new[] { OtherA, OtherB, OtherC }) AssertTrust(table, master, ModTrust.Untrusted, null);
    }

    [Fact]
    public void WithTheRequiringModRuleOffModsRequiringTheCleanedModAreUntrustedWithoutAReason()
    {
        var table = Classify(
            Settings(trustRequiringMods: false),
            Listing(CityMod, Skyrim),
            Listing(Addon, Skyrim, CleanedMod),
            Listing(CityPatch, Skyrim, CleanedMod, CityMod),
            Listing(Merged, Skyrim, CleanedMod, OtherA, OtherB, OtherC));

        foreach (var plugin in new[] { CityMod, Addon, CityPatch, Merged }) AssertTrust(table, plugin, ModTrust.Untrusted, null);
    }

    [Fact]
    public void RequiresCleanedModIsSetForEveryModRequiringItTrustedOrNot()
    {
        var table = Classify(
            Settings(never: [CityPatch]),
            Listing(CityMod, Skyrim),
            Listing(Addon, Skyrim, CleanedMod),
            Listing(CityPatch, Skyrim, CleanedMod, CityMod),
            Listing(Merged, Skyrim, CleanedMod, OtherA, OtherB, OtherC),
            MarkedListing(OtherGroupPatch, Skyrim, CleanedMod));

        var requiring = table.Plugins.Where(entry => entry.Value.RequiresCleanedMod).Select(entry => entry.Key).ToHashSet();

        Assert.Equal(new[] { Addon, CityPatch, Merged, OtherGroupPatch }.ToHashSet(), requiring);
    }

    [Fact]
    public void WithTheLoadBeforeRuleOnThePluginsBeforeTheCleanedModAreTrustedAndCounted()
    {
        var table = ClassifyInOrder(Settings(trustLoadingBefore: true), LoadOrderAroundTheCleanedMod());

        AssertTrust(table, CreationClub, ModTrust.Trusted, TrustReason.LoadsBeforeCleanedMod);
        AssertTrust(table, CityModMaster, ModTrust.Trusted, TrustReason.LoadsBeforeCleanedMod);
        AssertTrust(table, Skyrim, ModTrust.Trusted, TrustReason.BaseGame);
        AssertTrust(table, CleanedModMaster, ModTrust.Trusted, TrustReason.CleanedModMaster);
        AssertTrust(table, CityMod, ModTrust.Untrusted, null);
        Assert.Equal(2, table.LoadsBeforeCleanedModCount);
    }

    [Fact]
    public void WithTheLoadBeforeRuleOffThePluginsBeforeTheCleanedModAreUntrusted()
    {
        var table = ClassifyInOrder(Settings(trustLoadingBefore: false), LoadOrderAroundTheCleanedMod());

        AssertTrust(table, CreationClub, ModTrust.Untrusted, null);
        AssertTrust(table, CityModMaster, ModTrust.Untrusted, null);
        Assert.Equal(0, table.LoadsBeforeCleanedModCount);
    }

    [Fact]
    public void APluginCarryingThePatchMarkerIsSkippedByBothAutomaticRules()
    {
        var earlierGroupPatch = new PluginName("Earlier Group Patch.esp");
        var table = ClassifyInOrder(
            Settings(trustLoadingBefore: true),
            Listing(Skyrim),
            MarkedListing(earlierGroupPatch, Skyrim),
            Listing(CleanedMod, Skyrim),
            Listing(OtherA, Skyrim),
            MarkedListing(OtherGroupPatch, Skyrim, CleanedMod, OtherA));

        AssertTrust(table, OtherGroupPatch, ModTrust.Untrusted, TrustReason.OtherGroupPatch);
        AssertTrust(table, earlierGroupPatch, ModTrust.Untrusted, TrustReason.OtherGroupPatch);
        AssertTrust(table, OtherA, ModTrust.Untrusted, null);
    }

    [Fact]
    public void APluginCarryingThePatchMarkerFollowsTheListsLikeAnyOther()
    {
        var neverTrustedPatch = new PluginName("Never Trusted Group Patch.esp");
        var table = Classify(
            Settings(trusted: [OtherGroupPatch], never: [neverTrustedPatch]),
            MarkedListing(OtherGroupPatch, Skyrim, CleanedMod),
            MarkedListing(neverTrustedPatch, Skyrim, CleanedMod));

        AssertTrust(table, OtherGroupPatch, ModTrust.Trusted, TrustReason.Listed);
        AssertTrust(table, neverTrustedPatch, ModTrust.Untrusted, TrustReason.NeverTrusted);
    }

    [Fact]
    public void AMarkedPluginRequiringTheCleanedModAboveTheLimitHasTheTooManyMastersReason()
    {
        var table = Classify(
            Settings(),
            Listing(OtherA, Skyrim),
            Listing(OtherB, Skyrim),
            Listing(OtherC, Skyrim),
            MarkedListing(OtherGroupPatch, Skyrim, CleanedMod, OtherA, OtherB, OtherC));

        AssertTrust(table, OtherGroupPatch, ModTrust.Untrusted, TrustReason.RequiringModTooManyMasters);
    }

    [Fact]
    public void AMarkedPluginRequiringTheCleanedModIsUntrustedWithoutAReasonWhenNoAutomaticRuleWouldTrustIt()
    {
        var table = Classify(Settings(trustRequiringMods: false), MarkedListing(OtherGroupPatch, Skyrim, CleanedMod));

        AssertTrust(table, OtherGroupPatch, ModTrust.Untrusted, null);
        Assert.True(table.Plugins[OtherGroupPatch].RequiresCleanedMod);
    }

    [Fact]
    public void AMarkedPluginOnlyTheLoadBeforeRuleWouldTrustIsOtherGroupPatchNotRequiringAndNotCounted()
    {
        var table = ClassifyInOrder(
            Settings(trustLoadingBefore: true),
            Listing(Skyrim),
            Listing(CreationClub, Skyrim),
            MarkedListing(OtherGroupPatch, Skyrim),
            Listing(CleanedMod, Skyrim));

        AssertTrust(table, OtherGroupPatch, ModTrust.Untrusted, TrustReason.OtherGroupPatch);
        Assert.False(table.Plugins[OtherGroupPatch].RequiresCleanedMod);
        AssertTrust(table, CreationClub, ModTrust.Trusted, TrustReason.LoadsBeforeCleanedMod);
        Assert.Equal(1, table.LoadsBeforeCleanedModCount);
    }

    [Fact]
    public void ThePatchIsNotClassifiedWhenItCarriesTheMarker()
    {
        var table = Classify(Settings(), MarkedListing(Patch, Skyrim, CleanedMod));

        Assert.False(table.Plugins.ContainsKey(Patch));
    }

    [Fact]
    public void ListEntriesNamingTheCleanedModOrThePatchAreIgnored()
    {
        var table = Classify(
            Settings(trusted: [CleanedMod, Patch], ownChanges: [CleanedMod, Patch], never: [CleanedMod, Patch]),
            Listing(CityMod, Skyrim),
            Listing(Patch, Skyrim, CleanedMod, CityMod));

        AssertTrust(table, CleanedMod, ModTrust.Trusted, TrustReason.CleanedMod);
        AssertTrust(table, CityMod, ModTrust.Untrusted, null);
        Assert.False(table.Plugins.ContainsKey(Patch));
    }

    [Fact]
    public void ThePatchHasNoTrustEvenWhenItRequiresTheCleanedModOrIsAMasterOfATrustedPlugin()
    {
        var table = Classify(
            Settings(trusted: [CityMod]),
            Listing(Patch, Skyrim, CleanedMod),
            Listing(CityMod, Skyrim, Patch));

        Assert.False(table.Plugins.ContainsKey(Patch));
        Assert.True(table.IsPatch(Patch));
        Assert.False(table.IsPatch(CleanedMod));
    }

    private static PluginListing[] LoadOrderAroundTheCleanedMod() =>
    [
        Listing(Skyrim),
        Listing(CreationClub, Skyrim),
        Listing(CityModMaster, Skyrim),
        Listing(CleanedModMaster, Skyrim),
        Listing(CleanedMod, Skyrim, CleanedModMaster),
        Listing(CityMod, Skyrim, CityModMaster),
    ];

    private static ModTrustSettings Settings(
        PluginName[]? trusted = null,
        PluginName[]? ownChanges = null,
        PluginName[]? never = null,
        bool trustRequiringMods = true,
        bool trustLoadingBefore = false) =>
        new(CleanedMod, [.. trusted ?? []], [.. ownChanges ?? []], [.. never ?? []], trustRequiringMods, MaxOtherMasters, trustLoadingBefore);

    /// <summary>Classifies the cleaned mod and its masters followed by the given plugins.</summary>
    private static ModTrustTable Classify(ModTrustSettings settings, params PluginListing[] afterCleanedMod) =>
        ClassifyInOrder(settings, [.. CleanedModAndMasters, .. afterCleanedMod]);

    /// <summary>The patch goes last unless the given plugins list it themselves.</summary>
    private static ModTrustTable ClassifyInOrder(ModTrustSettings settings, params PluginListing[] listed)
    {
        PluginListing[] withPatch = listed.Any(listing => listing.Mod == Patch) ? listed : [.. listed, Listing(Patch)];
        var names = new KnownPluginNames(withPatch.SelectMany(listing => listing.Masters.Prepend(listing.Mod)));
        return ModTrustClassifier.Classify(TestLoadOrderPlugins.Of(names, Patch, [.. withPatch]), settings);
    }

    private static void AssertTrust(ModTrustTable table, PluginName plugin, ModTrust trust, TrustReason? reason)
    {
        var entry = table.Plugins[plugin];
        Assert.Equal((trust, reason), (entry.Trust, entry.Reason));
    }

    private static PluginListing Listing(PluginName plugin, params PluginName[] masters) =>
        new(plugin, Loaded: true, [.. masters]);

    private static PluginListing MarkedListing(PluginName plugin, params PluginName[] masters) =>
        Listing(plugin, masters) with { CarriesPatchMarker = true };
}
