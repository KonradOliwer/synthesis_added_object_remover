using System.Numerics;

namespace AddedObjectRemover;

/// <summary>
/// Direction from an invisible object to a neighbour, split by the world X (east) and Y (north)
/// axes through the invisible object. A neighbour exactly on an axis belongs to the side with the
/// larger coordinate: X = 0 counts as east, Y = 0 as north (so the same X/Y counts as north-east).
/// </summary>
internal enum Quadrant
{
    NorthEast,
    NorthWest,
    SouthWest,
    SouthEast,
}

/// <summary>Visible target objects around an invisible one and how many of them were removed, per quadrant.</summary>
internal sealed class QuadrantCounts
{
    private static readonly Quadrant[] Quadrants = Enum.GetValues<Quadrant>();

    private readonly int[] _visible = new int[Quadrants.Length];
    private readonly int[] _removed = new int[Quadrants.Length];

    public static IReadOnlyList<Quadrant> All => Quadrants;

    public int TotalVisible => _visible.Sum();

    public int TotalRemoved => _removed.Sum();

    /// <summary>0 when there are no visible neighbours.</summary>
    public float RemovedShare => TotalVisible == 0 ? 0f : (float)TotalRemoved / TotalVisible;

    /// <summary>True when every quadrant holding visible neighbours lost at least one of them.</summary>
    public bool EveryOccupiedQuadrantHasRemoval =>
        Quadrants.All(quadrant => Visible(quadrant) == 0 || Removed(quadrant) > 0);

    public static Quadrant QuadrantOf(Vector2 offset) => (offset.X >= 0, offset.Y >= 0) switch
    {
        (true, true) => Quadrant.NorthEast,
        (false, true) => Quadrant.NorthWest,
        (false, false) => Quadrant.SouthWest,
        (true, false) => Quadrant.SouthEast,
    };

    public void Add(Quadrant quadrant, bool removed)
    {
        _visible[(int)quadrant]++;
        if (removed) _removed[(int)quadrant]++;
    }

    public int Visible(Quadrant quadrant) => _visible[(int)quadrant];

    public int Removed(Quadrant quadrant) => _removed[(int)quadrant];

    /// <summary>E.g. "NorthEast 2/3, NorthWest 0/0, ..." as removed/visible.</summary>
    public string Describe() =>
        string.Join(", ", Quadrants.Select(quadrant => $"{quadrant} {Removed(quadrant)}/{Visible(quadrant)}"));
}
