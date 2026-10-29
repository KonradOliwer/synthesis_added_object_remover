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
    private const int LinkingPluginCount = 6;
    private const int WithoutLinksPosition = LinkingPluginCount / 2;
    private const int ManyWorkers = 8;

    private static readonly PluginName OtherPlugin = new("Other.esp");
    private static readonly WhatToCollect Plan = new(Navmesh: false, OtherModDetails: false, TerrainVersions: false, TerrainLayers: false, Water: false, MovedDistance: SettingDefaults.MovedDistanceInUnits, ProcessReplaced: false, JudgeOldPlaces: false, TerrainForChangedObjectsOnly: false);
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
        using var overlay = OpenCorrupted(broken);

        var read = LinkReader.ReadFromNonPlaced(
            [overlay], new HashSet<RecordKey> { TestTargets.Key(0) }, TestTargets.TargetName, CreateNames(broken), new Execution(1));

        var record = Assert.Single(read.Unreadable);
        Assert.Equal("Perk", record.RecordType);
        Assert.Equal("BrokenPerk", record.EditorId);
        Assert.Contains("expected parameter type flag", record.Problem);
        Assert.Equal("Location", Assert.Single(read.Links).SourceRecordType);
    }

    [Fact]
    public void ThePluginsLinksAndUnreadableRecordsComeInTheGivenPluginOrderWithOneOrManyWorkers()
    {
        var linking = Enumerable.Range(0, LinkingPluginCount).Select(CreateLinkingPlugin).ToArray();
        var withoutLinks = new FixtureMod("WithoutLinks.esp", _base, _target);
        var plugins = linking.Take(WithoutLinksPosition).Append(withoutLinks).Concat(linking.Skip(WithoutLinksPosition)).ToArray();
        var names = CreateNames(plugins);
        var records = Enumerable.Range(0, LinkingPluginCount).Select(index => TestTargets.Key(index)).ToHashSet();
        var overlays = new List<ISkyrimModDisposableGetter>();
        try
        {
            foreach (var plugin in plugins) overlays.Add(plugin == withoutLinks ? Open(plugin) : OpenCorrupted(plugin));

            var serial = LinkReader.ReadFromNonPlaced(overlays, records, TestTargets.TargetName, names, new Execution(1));
            var parallel = LinkReader.ReadFromNonPlaced(overlays, records, TestTargets.TargetName, names, new Execution(ManyWorkers));

            Assert.Equal(linking.SelectMany(ExpectedLinks), serial.Links.Select(link => (link.Source.Plugin, link.Target, link.Relation)));
            Assert.Equal(linking.Select(plugin => plugin.Key.ToPluginName()), serial.Unreadable.Select(record => record.Key.Plugin));
            Assert.Equal(serial.Links.ToArray(), parallel.Links.ToArray());
            Assert.Equal(serial.Unreadable.ToArray(), parallel.Unreadable.ToArray());
        }
        finally
        {
            foreach (var overlay in overlays) overlay.Dispose();
        }
    }

    [Fact]
    public void ThePluginsThatMayLinkAreReadInPriorityOrderHighestFirst()
    {
        var lower = CreatePluginLinkingTo("Lower.esp", 0);
        var unrelated = new FixtureMod("Unrelated.esp", _base);
        var higher = CreatePluginLinkingTo("Higher.esp", 1);
        var state = FixturePatcherState.Create([_base.Mod, _target.Mod, lower.Mod, unrelated.Mod, higher.Mod], _folder, _folder);
        var plugin = new PluginRecordsFactory(FixturePatcherState.CreateFinalWorld(state), ManyWorkers).Open();

        var read = plugin.ReadLinks(TestTargets.TargetName, new HashSet<RecordKey> { TestTargets.Key(0), TestTargets.Key(1) });

        Assert.Equal(new[] { higher.Key.ToPluginName(), lower.Key.ToPluginName() },read.Links.Select(link => link.Source.Plugin));
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

    /// <summary>
    /// A quest whose alias fills with a target NPC that is not checked, then a malformed perk between two lists that link to
    /// checked target records, so each plugin yields both kinds of link and an unreadable record.
    /// </summary>
    private FixtureMod CreateLinkingPlugin(int index)
    {
        var plugin = new FixtureMod($"Linking{index}.esp", _base, _target);
        plugin.Mod.Quests.Add(CreateQuestWithUniqueActor(plugin, UniqueActorIndexOf(index)));
        plugin.Mod.FormLists.Add(CreateListLinkingTo(plugin, index));
        plugin.Mod.Perks.Add(CreatePerk(plugin, $"BrokenPerk{index}"));
        plugin.Mod.FormLists.Add(CreateListLinkingTo(plugin, NextLinkingIndex(index)));
        return plugin;
    }

    /// <remarks>The game library reads the quest group before the form list group.</remarks>
    private static IEnumerable<(PluginName, RecordKey, RelationKind)> ExpectedLinks(FixtureMod plugin, int index)
    {
        var name = plugin.Key.ToPluginName();
        return
        [
            (name, TestTargets.Key(UniqueActorIndexOf(index)), RelationKind.UniqueActor),
            (name, TestTargets.Key(index), RelationKind.Other),
            (name, TestTargets.Key(NextLinkingIndex(index)), RelationKind.Other),
        ];
    }

    private static int UniqueActorIndexOf(int index) => LinkingPluginCount + index;

    private static int NextLinkingIndex(int index) => (index + 1) % LinkingPluginCount;

    private FixtureMod CreatePluginLinkingTo(string name, int targetIndex)
    {
        var plugin = new FixtureMod(name, _base, _target);
        plugin.Mod.FormLists.Add(CreateListLinkingTo(plugin, targetIndex));
        return plugin;
    }

    private static Quest CreateQuestWithUniqueActor(FixtureMod mod, int targetIndex)
    {
        var quest = new Quest(mod.NextKey(), FixtureMod.Release);
        quest.Aliases.Add(new QuestAlias { UniqueActor = new FormLinkNullable<INpcGetter>(TestTargets.FormKeyOf(targetIndex)) });
        return quest;
    }

    private static FormList CreateListLinkingTo(FixtureMod mod, int targetIndex)
    {
        var list = new FormList(mod.NextKey(), FixtureMod.Release);
        list.Items.Add(new FormLink<ISkyrimMajorRecordGetter>(TestTargets.FormKeyOf(targetIndex)));
        return list;
    }

    /// <summary>Writes the plugin with its first perk's parameter type broken and opens it as the game library reads load orders.</summary>
    private ISkyrimModDisposableGetter OpenCorrupted(FixtureMod mod)
    {
        var path = Write(mod);
        var bytes = File.ReadAllBytes(path);
        bytes[bytes.AsSpan().IndexOf(ParameterTypeTag) + ParameterTypeValueOffset] = NotAFloatParameterType;
        File.WriteAllBytes(path, bytes);
        return SkyrimMod.CreateFromBinaryOverlay(path, FixtureMod.Release);
    }

    private ISkyrimModDisposableGetter Open(FixtureMod mod) => SkyrimMod.CreateFromBinaryOverlay(Write(mod), FixtureMod.Release);

    private string Write(FixtureMod mod)
    {
        var path = Path.Combine(_folder, mod.Key.FileName);
        mod.Mod.WriteToBinary(path);
        return path;
    }

    private PluginNames CreateNames(params FixtureMod[] mods) =>
        new(new KnownPluginNames([_base.Key.ToPluginName(), _target.Key.ToPluginName(), .. mods.Select(mod => mod.Key.ToPluginName())]));
}
