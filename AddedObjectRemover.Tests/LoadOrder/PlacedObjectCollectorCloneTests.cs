using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>A clone an earlier run added to an Added Object Remover patch is one of the cleaned mod's objects, or only supports for another cleaned mod.</summary>
public class PlacedObjectCollectorCloneTests
{
    private static readonly PluginName BaseGame = new("Skyrim.esm");
    private static readonly PluginName OtherGroupPatch = new("Other Group Patch.esp");
    private static readonly PluginName OtherCleanedMod = new("Other Camp.esp");
    private static readonly PluginName LaterMod = new("Later.esp");
    private static readonly RecordKey Rock = new(BaseGame, 0x900);
    private static readonly WhatToCollect Plan = new(new ScanNeeds(), SettingDefaults.MovedDistanceInUnits, ProcessReplaced: false);
    private static readonly ModTrustTable Mods = TestModTrust.Of(
        TestTargets.TargetName, TestTargets.PatchName, trusted: [BaseGame], untrusted: [LaterMod], otherGroupPatches: [OtherGroupPatch]);
    private static readonly PlacedRecordFact Campfire = TestPlacedRecords.Create(TestTargets.Key(0), TestTargets.TargetName, Vector3.Zero);

    [Theory]
    [MemberData(nameof(Patches))]
    public void AMarkedRecordInAnyGroupsPatchIsACleanedModTargetThatNamesItsOriginal(string patch)
    {
        var clone = Clone(new PluginName(patch), TestTargets.TargetName);

        var collected = Collect(clone);

        var target = Assert.Single(collected.Targets, target => target.Key == clone.Key);
        Assert.Equal((TargetOrigin.CleanedMod, (RecordKey?)Rock), (target.Origin, target.CloneOf));
        Assert.Empty(collected.SupportOnlyObjects);
        Assert.Equal((1, 0), (collected.Counts.ClonesOfCleanedMod, collected.Counts.ClonesOfOtherCleanedMods));
    }

    public static TheoryData<string> Patches => [TestTargets.PatchName.FileName, OtherGroupPatch.FileName];

    [Fact]
    public void AClonePlacedForAnotherCleanedModIsOnlySupportWithoutAChange()
    {
        var clone = Clone(TestTargets.PatchName, OtherCleanedMod);

        var collected = Collect(clone);

        Assert.DoesNotContain(collected.Targets, target => target.Key == clone.Key);
        Assert.Empty(collected.OtherModObjects);
        var support = Assert.Single(collected.SupportOnlyObjects);
        Assert.Equal((clone.Key, (ObjectChange?)null), (support.Key, support.Change));
        Assert.Equal((0, 1, 0), (collected.Counts.ClonesOfCleanedMod, collected.Counts.ClonesOfOtherCleanedMods, collected.Counts.OthersUnchanged));
    }

    [Fact]
    public void ACloneALaterPluginOverridesIsATargetOverriddenLater()
    {
        var clone = Clone(TestTargets.PatchName, TestTargets.TargetName);
        var overridden = clone with { Versions = [clone.Winner, clone.Winner with { Plugin = LaterMod }] };

        var collected = Collect(overridden);

        Assert.DoesNotContain(collected.Targets, target => target.Key == clone.Key);
        Assert.Equal((1, 1), (collected.Counts.TargetsOverriddenLater, collected.Counts.ClonesOfCleanedMod));
    }

    /// <summary>
    /// A clone another group's patch created, which this group's patch carries: judged where it stands, unless a later plugin moved it
    /// below this group's patch; then that plugin is the winner past the patches and the clone is left to it, like the cleaned mod's
    /// own records.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACloneThisGroupsPatchCarriesIsLeftToALaterPluginThatMovedItBelowThePatch(bool laterPluginMovedIt)
    {
        var clone = Clone(OtherGroupPatch, TestTargets.TargetName);
        PlacedVersionFact[] below = laterPluginMovedIt
            ? [clone.Winner, clone.Winner with { Plugin = LaterMod, Placement = clone.Placement with { Position = new Vector3(50, 1, 1) } }]
            : [clone.Winner];
        var carried = clone with { Versions = [.. below, below[^1] with { Plugin = TestTargets.PatchName }] };

        var collected = Collect(carried);

        Assert.Equal(!laterPluginMovedIt, collected.Targets.Any(target => target.Key == clone.Key));
        Assert.Equal(laterPluginMovedIt, collected.CleanedModsObjectsOverriddenLater.Any(overridden => overridden.Key == clone.Key));
    }

    [Fact]
    public void AMarkerInAPluginThatIsNoPatchIsAnOrdinaryEditorId()
    {
        var marked = Clone(LaterMod, TestTargets.TargetName);

        var collected = Collect(marked);

        Assert.DoesNotContain(collected.Targets, target => target.Key == marked.Key);
        Assert.Contains(collected.OtherModObjects, other => other.Key == marked.Key);
        Assert.Equal(0, collected.Counts.ClonesOfCleanedMod);
        Assert.Empty(collected.BrokenCloneMarkers);
    }

    [Fact]
    public void ABrokenMarkerInAPatchIsNotedAndTheRecordOnlySupportsLikeAnotherCleanedModsClone()
    {
        var broken = TestPlacedRecords.Create(new RecordKey(TestTargets.PatchName, 0x801), TestTargets.PatchName, Vector3.One) with { EditorId = "AORClone_broken" };

        var collected = Collect(broken);

        Assert.Equal(broken.Key, Assert.Single(collected.BrokenCloneMarkers));
        Assert.DoesNotContain(collected.OtherModObjects, other => other.Key == broken.Key);
        Assert.DoesNotContain(collected.Targets, target => target.Key == broken.Key);
        Assert.Null(Assert.Single(collected.SupportOnlyObjects, other => other.Key == broken.Key).Change);
    }

    [Fact]
    public void ALinkIntoACloneIsKeptLikeALinkIntoTheCleanedModsOwnObjects()
    {
        var clone = Clone(TestTargets.PatchName, TestTargets.TargetName);
        var link = new LinkFact(Campfire.Key, null, "PlacedObject", clone.Key, RelationKind.EnableParent, SourceIsPlaced: true, SourceIsWorldspace: false, default);
        var plugin = new FixedFactsPlugin(ImmutableArray.Create(Campfire, clone), [link]);

        var collected = PlacedObjects.Collect(plugin, Mods, Plan);

        Assert.Equal(link, Assert.Single(collected.Links));
    }

    [Fact]
    public void ARemovedCloneOfTheCleanedModCountsWithTheCleanedModsRemovedRecords()
    {
        var clone = TestPlacedRecords.WithWinningPresence(
            Clone(TestTargets.PatchName, TestTargets.TargetName), new PresenceFacts(Deleted: false, InitiallyDisabled: true, EnableParent: null));

        var collected = Collect(clone);

        Assert.Equal((1, 1), (collected.Counts.RemovedByEarlierPatchers, collected.Counts.ClonesOfCleanedMod));
    }

    private static PlacedRecordFact Clone(PluginName patch, PluginName cleanedMod) =>
        TestPlacedRecords.Create(new RecordKey(patch, 0x800), patch, Vector3.One) with { EditorId = CloneMarker.Of(Rock, cleanedMod) };

    private static CollectedObjects Collect(PlacedRecordFact record) =>
        PlacedObjects.Collect(new FixedFactsPlugin(ImmutableArray.Create(Campfire, record)), Mods, Plan);
}
