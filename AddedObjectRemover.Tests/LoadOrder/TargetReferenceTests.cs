using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.LoadOrder;

public class TargetReferenceTests
{
    private static readonly ModKey OtherMod = ModKey.FromNameAndExtension("Other.esp");

    [Fact]
    public void LinkFromTargetObjectJoinsAGroup()
    {
        var source = WithEnableParent(TestTargets.Key(0), TestTargets.Key(1));
        var references = new Dictionary<FormKey, KeepReason>();
        var links = new List<TargetLink>();

        TargetReferenceCollector.CollectFromPlaced(source, isTargetObject: true, TestTargets.TargetMod, references, links);

        Assert.Equal(new[] { new TargetLink(TestTargets.Key(0), TestTargets.Key(1)) }, links);
        Assert.Empty(references);
    }

    [Fact]
    public void LinkFromOtherPlacedRecordKeepsTheTarget()
    {
        var source = WithEnableParent(new FormKey(OtherMod, 0x900), TestTargets.Key(1));
        var references = new Dictionary<FormKey, KeepReason>();
        var links = new List<TargetLink>();

        TargetReferenceCollector.CollectFromPlaced(source, isTargetObject: false, TestTargets.TargetMod, references, links);

        Assert.Empty(links);
        var reason = references[TestTargets.Key(1)];
        Assert.Equal(KeepKind.PlacedReference, reason.Kind);
        Assert.Equal("placed object: Enable Parent", reason.Category);
    }

    [Fact]
    public void LinkToAnotherPluginIsIgnored()
    {
        var source = WithEnableParent(TestTargets.Key(0), new FormKey(OtherMod, 0x901));
        var references = new Dictionary<FormKey, KeepReason>();
        var links = new List<TargetLink>();

        TargetReferenceCollector.CollectFromPlaced(source, isTargetObject: true, TestTargets.TargetMod, references, links);

        Assert.Empty(links);
        Assert.Empty(references);
    }

    [Fact]
    public void NonPlacedRecordLinkKeepsTheTarget()
    {
        var mod = new SkyrimMod(OtherMod, SkyrimRelease.SkyrimSE);
        var list = new FormList(new FormKey(OtherMod, 0x902), SkyrimRelease.SkyrimSE) { EditorID = "OtherList" };
        list.Items.Add(new FormLink<ISkyrimMajorRecordGetter>(TestTargets.Key(2)));
        mod.FormLists.Add(list);
        var references = new Dictionary<FormKey, KeepReason>();

        TargetReferenceCollector.CollectFromNonPlaced([mod], new HashSet<FormKey> { TestTargets.Key(2), TestTargets.Key(3) }, references);

        Assert.Equal(new[] { TestTargets.Key(2) }, references.Keys);
        Assert.Equal(KeepKind.NonPlacedReference, references[TestTargets.Key(2)].Kind);
        Assert.Equal("FormList record", references[TestTargets.Key(2)].Category);
    }

    private static PlacedObject WithEnableParent(FormKey source, FormKey parent)
    {
        var enableParent = new EnableParent();
        enableParent.Reference.SetTo(parent);
        return new PlacedObject(source, SkyrimRelease.SkyrimSE) { EnableParent = enableParent };
    }
}
