using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>Each other object carries what depends on it or controls it, read from its record and the records linking to it.</summary>
public class PlacedObjectCollectorRolesTests
{
    private static readonly PluginName BaseGame = new("Skyrim.esm");
    private static readonly RecordKey Lantern = new(BaseGame, 0x900);
    private static readonly WhatToCollect Plan = new(Navmesh: false, OtherModDetails: false, TerrainVersions: false, TerrainLayers: false, Water: false, MovedDistance: SettingDefaults.MovedDistanceInUnits);
    private static readonly ModTrustTable Mods = TestModTrust.Of(TestTargets.TargetName, TestTargets.PatchName, trusted: [BaseGame], untrusted: [], trustedForOwnChanges: []);
    private static readonly PlacedRecordFact Target = TestPlacedRecords.Create(TestTargets.Key(0), TestTargets.TargetName, Vector3.Zero);

    [Fact]
    public void AnObjectWithoutRolesHasNone() =>
        Assert.Equal(ReferenceRoles.None, RolesOf(TestPlacedRecords.Create(Lantern, BaseGame, Vector3.One), linkedFrom: []));

    [Fact]
    public void TheRolesComeFromTheRecordAndTheRecordsLinkingToIt()
    {
        var record = TestPlacedRecords.WithWinningPresence(
            TestPlacedRecords.Create(Lantern, BaseGame, Vector3.One) with
            {
                IsPersistent = true,
                IsTeleportDoor = true,
                NamedByScript = new ScriptNamedRecord(new RecordKey(BaseGame, 0x123), "SomeQuest", ScriptNamedKind.Quest),
                HasLocationRefType = true,
            },
            new PresenceFacts(Deleted: false, InitiallyDisabled: false, new EnableParentFact(new RecordKey(BaseGame, 0x901), Opposite: false)));

        Assert.Equal(
            ReferenceRoles.Persistent | ReferenceRoles.TeleportDoor | ReferenceRoles.HasEnableParent | ReferenceRoles.ScriptNamesQuestOrScene
            | ReferenceRoles.LocationRefType | ReferenceRoles.LinkedFromPlacedRecord,
            RolesOf(record, linkedFrom: [Lantern]));
    }

    private static ReferenceRoles RolesOf(PlacedRecordFact record, HashSet<RecordKey> linkedFrom)
    {
        var plugin = new FixedFactsPlugin(ImmutableArray.Create(Target, record)) { LinkedFrom = linkedFrom };
        return Assert.Single(PlacedObjectCollector.Collect(plugin, Mods, Plan).SupportOnlyObjects).Roles;
    }
}
