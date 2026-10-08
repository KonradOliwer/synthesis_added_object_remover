using System.Numerics;

namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>Target-plugin object that may be removed.</summary>
/// <param name="Cell">Null for an interior, whose cell is the space itself.</param>
/// <param name="Rotation">The placement's Euler angles in radians, as the record stores them.</param>
/// <param name="HasMapMarker">The placed reference itself carries map marker data (XMRK).</param>
/// <param name="ReferenceRadius">The reference's own radius (XRDS) as the record stores it; null when it has none.</param>
/// <param name="PrimitiveBounds">The reference's primitive box, full sizes as the record exposes them; null when it is not a primitive.</param>
public sealed record TargetObject(
    TargetId Id,
    RecordKey Key,
    string? EditorId,
    RecordKey SpaceKey,
    CellFact? Cell,
    PlacedTransform Transform,
    Vector3 Rotation,
    BaseKey? Base,
    bool IsTeleportDoor,
    bool IsPrimitive,
    bool HasMapMarker,
    float? ReferenceRadius,
    Vector3? PrimitiveBounds);

/// <summary>Compact snapshot of another mod's placed object (winning version).</summary>
/// <param name="HasMapMarker">The placed reference itself carries map marker data (XMRK), e.g. a fast-travel marker.</param>
public readonly record struct OtherObject(
    OtherId Id,
    RecordKey Key,
    RecordKey SpaceKey,
    PluginName WinningMod,
    string? EditorId,
    BaseKey? Base,
    Vector3 Position,
    Vector3 Rotation,
    float Scale,
    bool IsPrimitive,
    bool HasMapMarker)
{
    public PlacedTransform Transform => new(Position, Mat3.FromEuler(Rotation), Scale);

    /// <summary>A placed NPC or creature (ACHR), the only placed type whose base link is an NPC.</summary>
    public bool IsPlacedNpc => Base is { Kind: BaseLinkKind.Npc };
}
