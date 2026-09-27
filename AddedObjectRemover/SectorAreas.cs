using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// One of 8 equal 45° directions around an invisible object, by the world X (east) and Y (north)
/// axes. Each spans 22.5° to either side of its compass direction; an angle exactly on a boundary
/// belongs to the direction counter-clockwise of it (22.5° from east is NorthEast). An offset with
/// no horizontal part at all counts as <see cref="SectorAreas.NoHorizontalOffset"/>.
/// </summary>
internal enum DirectionSector
{
    East,
    NorthEast,
    North,
    NorthWest,
    West,
    SouthWest,
    South,
    SouthEast,
}

internal enum SectorState
{
    Empty,
    Kept,
    Removed,
}

/// <summary>
/// Ground area of the visible target objects around an invisible object and how much of it was
/// removed, per direction. A direction without objects is empty; otherwise it is removed when its
/// removed share of the area reaches the threshold.
/// </summary>
internal sealed class SectorAreas(int thresholdPercent)
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

    private readonly float[] _total = new float[SectorCount];
    private readonly float[] _removed = new float[SectorCount];
    private readonly int[] _objects = new int[SectorCount];
    private readonly int[] _removedObjects = new int[SectorCount];

    public static IReadOnlyList<DirectionSector> All => Sectors;

    public int OccupiedCount => Sectors.Count(sector => State(sector) != SectorState.Empty);

    public int RemovedCount => Sectors.Count(sector => State(sector) == SectorState.Removed);

    public static DirectionSector SectorOf(Vector2 offset)
    {
        if (offset == Vector2.Zero) return NoHorizontalOffset;
        var degrees = float.RadiansToDegrees(MathF.Atan2(offset.Y, offset.X));
        var fromEast = degrees < 0 ? degrees + FullTurnDegrees : degrees;
        return (DirectionSector)((int)MathF.Floor(fromEast / SectorDegrees + CentredSectorShift) % SectorCount);
    }

    public void Add(DirectionSector sector, float area, bool removed)
    {
        _total[(int)sector] += area;
        _objects[(int)sector]++;
        if (!removed) return;
        _removed[(int)sector] += area;
        _removedObjects[(int)sector]++;
    }

    public float Total(DirectionSector sector) => _total[(int)sector];

    public float Removed(DirectionSector sector) => _removed[(int)sector];

    /// <summary>A direction whose objects all have no ground area is judged by object count instead.</summary>
    public SectorState State(DirectionSector sector)
    {
        var index = (int)sector;
        if (_objects[index] == 0) return SectorState.Empty;
        var reachesThreshold = _total[index] > 0
            ? _removed[index] * Percent.PerWhole >= thresholdPercent * _total[index]
            : _removedObjects[index] * Percent.PerWhole >= thresholdPercent * _objects[index];
        return reachesThreshold ? SectorState.Removed : SectorState.Kept;
    }

    /// <summary>E.g. "East 120/400, NorthEast -, ..." as removed/total ground area; "-" for an empty direction.</summary>
    public string Describe() =>
        string.Join(", ", Sectors.Select(sector =>
            State(sector) == SectorState.Empty ? $"{sector} -" : $"{sector} {Removed(sector):F0}/{Total(sector):F0}"));
}
