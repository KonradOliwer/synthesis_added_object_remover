using System.Numerics;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>One separate stretch of the interior (or of the worldspace) per scene.</summary>
internal enum FixtureSlot
{
    TooClose,
    TwinPillars,
    BoundsOnly,
    NpcStuck,
    NpcClear,
    Replacement,
    LinkedGroup,
    ReferencedByPlaced,
    ReferencedByNonPlaced,
    TeleportDoors,
    TouchChain,
    SupportLoss,
    LeftoverInside,
    LeftoverLight,
    LeftoverSound,
    Standing,
}

internal static class FixtureLayout
{
    /// <summary>Well beyond the leftover look-around radius, so scenes never see each other.</summary>
    private const float SlotSpacing = 4000f;

    /// <summary>How far a scene puts objects it wants away from everything else.</summary>
    public const float FarDistance = 1500f;

    /// <summary>
    /// Where a pad sits beside a crate: clear of the crate's surface, but inside the removal zone
    /// (half a crate size beyond the crate) that both zone shapes use at the default distance.
    /// </summary>
    public static readonly Vector3 PinningPadOffset = new(90f, 0f, 0f);

    public static readonly Vector3 FarEast = new(FarDistance, 0f, 0f);
    public static readonly Vector3 FarNorth = new(0f, FarDistance, 0f);

    public static Vector3 Origin(FixtureSlot slot) => new((int)slot * SlotSpacing, 0f, 0f);
}
