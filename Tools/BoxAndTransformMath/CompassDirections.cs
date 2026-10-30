using System.Numerics;

namespace AddedObjectRemover;

public static class CompassDirections
{
    public const int SectorCount = 8;

    /// <summary>
    /// The direction of an offset that has no horizontal part at all (exactly zero), such as an
    /// object whose box centre lies exactly straight above or below: any fixed direction will do,
    /// as long as the object counts in exactly one.
    /// </summary>
    public const DirectionSector NoHorizontalOffset = DirectionSector.East;

    private const float FullTurnDegrees = 360f;
    private const float SectorDegrees = FullTurnDegrees / SectorCount;

    /// <summary>Half a sector, in sectors: each sector is centred on its direction.</summary>
    private const float CentredSectorShift = 0.5f;

    private static readonly DirectionSector[] Sectors = Enum.GetValues<DirectionSector>();

    public static IReadOnlyList<DirectionSector> All => Sectors;

    public static DirectionSector Of(Vector2 offset)
    {
        if (offset == Vector2.Zero) return NoHorizontalOffset;
        var degrees = float.RadiansToDegrees(MathF.Atan2(offset.Y, offset.X));
        var fromEast = degrees < 0 ? degrees + FullTurnDegrees : degrees;
        return (DirectionSector)((int)MathF.Floor(fromEast / SectorDegrees + CentredSectorShift) % SectorCount);
    }

    /// <summary>
    /// The direction from <paramref name="point"/> towards a box that does not contain it: towards
    /// the box centre's horizontal offset when the box lies straight above or below the point
    /// (however small that offset), else towards the box's closest point.
    /// </summary>
    public static DirectionSector Towards(Vector3 point, OrientedBox box)
    {
        var towards = box.IsCrossedByVerticalLine(point) ? box.Center : box.ClosestPoint(point);
        return Of(new Vector2(towards.X - point.X, towards.Y - point.Y));
    }
}
