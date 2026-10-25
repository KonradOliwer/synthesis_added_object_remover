using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>A record the game library cannot parse is reported once per run and the rest is still read; no object is kept because of it.</summary>
public class UnreadableRecordTests : IDisposable
{
    private static readonly byte[] ParameterTypeTag = "EPFT"u8.ToArray();
    private const int ParameterTypeValueOffset = 6;
    private const byte NotAFloatParameterType = 5;

    private static readonly PluginName OtherPlugin = new("Other.esp");
    private static readonly WhatToCollect Plan = new(Navmesh: false, OtherModDetails: false, TerrainVersions: false, TerrainLayers: false, Water: false, MovedDistance: SettingDefaults.MovedDistanceInUnits);
    private static readonly ModTrustTable Mods = TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [], untrusted: [OtherPlugin]);

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
        Assert.Equal("Location", Assert.Single(facts).SourceRecordType);
    }

    [Fact]
    public void NoObjectIsKeptOrLinkedBecauseARecordIsUnreadable()
    {
        var target = TestPlacedRecords.Create(TestTargets.Key(0), TestTargets.TargetName, Vector3.Zero);
        var broken = Unreadable();

        var collected = PlacedObjects.Collect(new FixedFactsPlugin([target]) { Unreadable = [broken] }, Mods, Plan);

        Assert.Empty(collected.Links);
        Assert.Equal(broken, Assert.Single(collected.UnreadableRecords));
    }

    [Fact]
    public void TheLogNamesTheUnreadableRecordWithItsPluginAndWhatTheLibraryReportedWhetherOrNotAnyCheckedObjectIsLinkable()
    {
        var world = TestScenes.CreateWorld([], []) with { UnreadableRecords = [Unreadable()] };

        var lines = TestReport.WithoutProblems().CollectedSectionLines(world);

        Assert.Equal(
            "Warning: could not read Perk BrokenPerk [000010:Other.esp]: it is malformed. Its links were not all checked.",
            Assert.Single(lines, line => line.StartsWith("Warning")));
        Assert.DoesNotContain(
            TestReport.WithoutProblems().CollectedSectionLines(TestScenes.CreateWorld([], [])),
            line => line.StartsWith("Warning"));
    }

    [Fact]
    public void EachRunOfABatchNamesTheRecordInItsOwnLog()
    {
        var world = TestScenes.CreateWorld([], []) with { UnreadableRecords = [Unreadable()] };

        var first = TestReport.WithoutProblems().CollectedSectionLines(world);
        var second = TestReport.WithoutProblems().CollectedSectionLines(world);

        Assert.Single(first, line => line.StartsWith("Warning"));
        Assert.Equal(first, second);
    }

    private static UnreadableRecord Unreadable() => new(new RecordKey(OtherPlugin, 0x10), "BrokenPerk", "Perk", "it is malformed");

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
