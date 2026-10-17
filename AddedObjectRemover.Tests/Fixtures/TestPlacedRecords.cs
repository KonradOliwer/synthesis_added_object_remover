using System.Numerics;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Placed record facts in the test target space, shown in game, unrotated.</summary>
internal static class TestPlacedRecords
{
    public static PlacedRecordFact Create(RecordKey key, PluginName winningMod, Vector3 position, BaseKey? baseKey = null) =>
        new(
            key,
            EditorId: null,
            winningMod,
            new SpaceFact(TestTargets.Space, null, SpaceKind.Worldspace),
            new CellFact(TestTargets.Space, null),
            InTargetSpace: true,
            new PlacementFacts(InitiallyDisabled: false, HasEnableParent: false, position, Vector3.Zero),
            Scale: null,
            Base: baseKey,
            IsTeleportDoor: false,
            IsPrimitive: false,
            HasMapMarker: false,
            IsPersistent: false,
            NamedByScript: null,
            HasLocationRefType: false,
            ReferenceRadius: null,
            PrimitiveBounds: null);
}
