using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>A record the game library cannot parse is reported, the rest is still read, and the objects it may link to are kept.</summary>
public class UnreadableRecordTests : IDisposable
{
    private static readonly byte[] ParameterTypeTag = "EPFT"u8.ToArray();
    private const int ParameterTypeValueOffset = 6;
    private const byte NotAFloatParameterType = 5;

    private static readonly PluginName OtherPlugin = new("Other.esp");
    private static readonly WhatToCollect Plan = new(SupportOnlyObjects: false, Terrain: false, Navmesh: false, OtherModDetails: false, TerrainVersions: false, TerrainLayers: false, Water: false, MovedDistance: SettingDefaults.MovedDistanceInUnits);
    private static readonly ModTrustTable Mods = TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [], untrusted: [OtherPlugin]);

    private static readonly ReportContext Context = new(null!, null!, Detailed: false);

    private readonly string _folder = Directory.CreateTempSubdirectory().FullName;
    private readonly FixtureMod _base = new("Base.esm");
    private readonly FixtureMod _target;

    public UnreadableRecordTests()
    {
        _target = new FixtureMod(TestTargets.TargetName.ToString(), _base);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void APluginIsReadForLinksOnlyWhenItIsOrMastersAPluginOfTheCheckedRecords()
    {
        var unrelated = new FixtureMod("Unrelated.esp", _base);
        var master = new FixtureMod("Master.esp", _base);
        var child = new FixtureMod("Child.esp", _base, _target);
        var names = CreateNames(unrelated, master, child);
        var checkedPlugins = new HashSet<PluginName> { _target.Key.ToPluginName() };

        Assert.True(LinkablePlugins.MayLinkToAny(_target.Mod, checkedPlugins, FixturePatcherState.PatchModKey, names));
        Assert.True(LinkablePlugins.MayLinkToAny(child.Mod, checkedPlugins, FixturePatcherState.PatchModKey, names));
        Assert.False(LinkablePlugins.MayLinkToAny(unrelated.Mod, checkedPlugins, FixturePatcherState.PatchModKey, names));
        Assert.False(LinkablePlugins.MayLinkToAny(master.Mod, checkedPlugins, FixturePatcherState.PatchModKey, names));
        Assert.True(LinkablePlugins.MayLinkToAny(new FixtureMod(FixturePatcherState.PatchModKey.FileName).Mod, checkedPlugins, FixturePatcherState.PatchModKey, names));
    }

    [Fact]
    public void AMalformedRecordIsReportedAndTheRecordsAfterItAreStillRead()
    {
        var broken = new FixtureMod("Broken.esp", _base, _target);
        broken.Mod.Perks.Add(CreatePerk(broken, "BrokenPerk"));
        var location = new Location(broken.NextKey(), FixtureMod.Release);
        location.WorldLocationMarkerRef.SetTo(TestTargets.FormKeyOf(0));
        broken.Mod.Locations.Add(location);
        var facts = new List<LinkFact>();

        var unreadable = ReadFromCorrupted(broken, facts);

        var record = Assert.Single(unreadable);
        Assert.Equal("Perk", record.RecordType);
        Assert.Equal("BrokenPerk", record.EditorId);
        Assert.Contains("expected parameter type flag", record.Problem);
        Assert.Equal(new HashSet<PluginName> { _target.Key.ToPluginName(), broken.Key.ToPluginName() }, record.LinkablePlugins);
        Assert.Equal("Location", Assert.Single(facts).SourceRecordType);
    }

    [Fact]
    public void AnUnreadableRecordMayLinkToEveryCheckedObjectOfItsOwnPluginAndMasters()
    {
        var target = TestPlacedRecords.Create(TestTargets.Key(0), TestTargets.TargetName, Vector3.Zero);
        var mayLink = Unreadable(TestTargets.TargetName);
        var cannotLink = Unreadable(OtherPlugin);

        var linked = PlacedObjectCollector.Collect(new FixedFactsPlugin([target]) { Unreadable = [mayLink] }, Mods, Plan);
        var notLinked = PlacedObjectCollector.Collect(new FixedFactsPlugin([target]) { Unreadable = [cannotLink] }, Mods, Plan);

        var link = Assert.Single(linked.Links);
        Assert.Equal((mayLink.Key, target.Key, RelationKind.UnreadableRecord), (link.Source, link.Target, link.Relation));
        Assert.Empty(notLinked.Links);
        Assert.Equal(mayLink, Assert.Single(linked.UnreadableRecords));
    }

    [Fact]
    public void AnUnreadableRecordKeepsTheObjectItMayLinkToWithAReasonThatSaysSo()
    {
        var link = new LinkFact(
            new RecordKey(OtherPlugin, 0x10), "BrokenPerk", "Perk", TestTargets.Key(0), RelationKind.UnreadableRecord,
            SourceIsPlaced: false, SourceIsWorldspace: false, AliasFillFlags.None);

        var outcome = LinkRule.DecideLinkOutcome(TestTargets.CreateMany(1), [link]);

        var reason = Assert.IsType<KeepReason>(Assert.Single(outcome.References));
        Assert.Equal(KeepKind.UnreadableRecord, reason.Kind);
        Assert.Equal("record that could not be read", KeepReasonText.Category(reason));
        Assert.Contains("could not be read and may link to it", KeepReasonText.Detail(reason));
    }

    [Fact]
    public void TheLogNamesEachUnreadableRecordWithItsPluginAndWhatTheLibraryReported()
    {
        var world = TestScenes.CreateWorld([], []) with { UnreadableRecords = [Unreadable(OtherPlugin)] };

        var lines = LogSections.BuildScanSection(world, TestTargets.TargetName, TimeSpan.Zero, Context).Lines;

        Assert.Equal(
            "Warning: could not read Perk BrokenPerk [000010:Other.esp]: it is malformed. The objects to check that it may link to are kept.",
            lines[^1]);
        Assert.DoesNotContain(
            LogSections.BuildScanSection(TestScenes.CreateWorld([], []), TestTargets.TargetName, TimeSpan.Zero, Context).Lines,
            line => line.StartsWith("Warning"));
    }

    private static UnreadableRecord Unreadable(PluginName linkable) =>
        new(new RecordKey(OtherPlugin, 0x10), "BrokenPerk", "Perk", "it is malformed", new HashSet<PluginName> { linkable });

    private static Perk CreatePerk(FixtureMod mod, string editorId) => new(mod.NextKey(), FixtureMod.Release)
    {
        EditorID = editorId,
        Effects = [new PerkEntryPointModifyActorValue { Value = 1f }],
    };

    private ImmutableArray<UnreadableRecord> ReadFromCorrupted(FixtureMod mod, List<LinkFact> facts)
    {
        var path = Path.Combine(_folder, mod.Key.FileName);
        mod.Mod.WriteToBinary(path);
        var bytes = File.ReadAllBytes(path);
        bytes[bytes.AsSpan().IndexOf(ParameterTypeTag) + ParameterTypeValueOffset] = NotAFloatParameterType;
        File.WriteAllBytes(path, bytes);
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(path, FixtureMod.Release);
        return LinkReader.ReadFromNonPlaced(
            [overlay], new HashSet<RecordKey> { TestTargets.Key(0) }, TestTargets.TargetName, CreateNames(mod), facts);
    }

    private PluginNames CreateNames(params FixtureMod[] mods) =>
        new(new KnownPluginNames([_base.Key.ToPluginName(), _target.Key.ToPluginName(), .. mods.Select(mod => mod.Key.ToPluginName())]));
}
