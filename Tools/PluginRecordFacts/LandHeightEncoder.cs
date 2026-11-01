namespace AddedObjectRemover;

/// <summary>The height map (VHGT) of a LAND record as stored.</summary>
/// <param name="Offset">In height steps.</param>
/// <param name="Steps">One per grid point, row by row from the bottom, x fastest.</param>
public sealed record LandHeightSteps(float Offset, sbyte[] Steps);

/// <summary>
/// Encodes the heights at a LAND record's grid points as its height map (VHGT) stores them: an offset in height steps of
/// <see cref="UnitsPerStep"/> units, then per row one signed byte step from the first point of the row before (the first row from
/// the offset), and per other point one step from the point before it in its row.
/// </summary>
public static class LandHeightEncoder
{
    public const float UnitsPerStep = 8f;
    private const int VerticesPerSide = TerrainHeights.VerticesPerSide;

    /// <param name="heights">In game units, row by row from the bottom, x fastest.</param>
    /// <returns>
    /// Null when a step does not fit in a signed byte, so the heights cannot be stored; otherwise each point is stored within half a
    /// step of its height, and exactly when its height lies a whole number of steps from the first point's.
    /// </returns>
    public static LandHeightSteps? Encode(IReadOnlyList<float> heights)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(heights.Count, PointSet.PointCount);
        var offset = heights[0] / UnitsPerStep;
        var steps = new sbyte[heights.Count];
        var rowStart = 0;
        for (var y = 0; y < VerticesPerSide; y++)
        {
            var first = y * VerticesPerSide;
            var previous = rowStart;
            for (var x = 0; x < VerticesPerSide; x++)
            {
                var value = StepsAboveOffset(heights[first + x], offset);
                if (!TryStore(value - previous, out steps[first + x])) return null;
                previous = value;
                if (x == 0) rowStart = value;
            }
        }
        return new LandHeightSteps(offset, steps);
    }

    public static bool CanStore(IReadOnlyList<float> heights) => Encode(heights) is not null;

    /// <summary>
    /// The height a whole number of steps from <paramref name="storedHeight"/> nearest to <paramref name="height"/>, a half step
    /// rounding up. A record storing <paramref name="storedHeight"/> stores it exactly, and so does every record whose stored heights
    /// lie whole steps from it.
    /// </summary>
    public static float NearestOnTheStepsOf(float height, float storedHeight) =>
        storedHeight + UnitsPerStep * MathF.Floor((height - storedHeight) / UnitsPerStep + 0.5f);

    private static int StepsAboveOffset(float height, float offset) => Convert.ToInt32(height / UnitsPerStep - offset);

    private static bool TryStore(int step, out sbyte stored)
    {
        var fits = step is >= sbyte.MinValue and <= sbyte.MaxValue;
        stored = fits ? (sbyte)step : default;
        return fits;
    }
}
