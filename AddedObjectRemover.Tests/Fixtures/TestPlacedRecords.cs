using System.Numerics;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Placed record facts in the test target space, shown in game, unrotated, with the winning version as their only version.</summary>
internal static class TestPlacedRecords
{
    public static PlacedRecordFact Create(RecordKey key, PluginName winningMod, Vector3 position, BaseKey? baseKey = null)
    {
        var placement = new PlacementFacts(new PresenceFacts(Deleted: false, InitiallyDisabled: false, EnableParent: null), position, Vector3.Zero);
        return new(
            key,
            EditorId: null,
            new SpaceFact(TestTargets.Space, null, SpaceKind.Worldspace, Name: null),
            new CellFact(TestTargets.Space, null),
            InTargetSpace: true,
            IsTeleportDoor: false,
            IsPrimitive: false,
            HasMapMarker: false,
            IsPersistent: false,
            NamedByScript: null,
            HasLocationRefType: false,
            ReferenceRadius: null,
            PrimitiveBounds: null,
            Versions: [new PlacedVersionFact(winningMod, placement, Scale: null, baseKey, InTheSpace)]);
    }

    /// <summary>Where every version of these records sits: the temporary list of the test space's own cell.</summary>
    public static readonly VersionLocation InTheSpace = new(TestTargets.Space, TestTargets.Space, InPersistentList: false);

    /// <summary>The record with its winning version's presence flags replaced.</summary>
    public static PlacedRecordFact WithWinningPresence(PlacedRecordFact record, PresenceFacts presence) =>
        record with { Versions = record.Versions.SetItem(record.Versions.Length - 1, record.Winner with { Placement = record.Placement with { Presence = presence } }) };
}
