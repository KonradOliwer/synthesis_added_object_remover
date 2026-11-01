namespace AddedObjectRemover;

/// <summary>Compares two height grids of one cell point by point.</summary>
public static class HeightGridComparison
{
    /// <param name="one">Null where the version has no height map.</param>
    /// <param name="other">Null where the version has no height map.</param>
    /// <returns>Whether the heights at the point lie more than the tolerance apart; a grid next to a missing one differs, two missing ones do not.</returns>
    public static bool DiffersAt(float[]? one, float[]? other, int point, float tolerance) => (one, other) switch
    {
        (null, null) => false,
        ({ } oneHeights, { } otherHeights) => MathF.Abs(otherHeights[point] - oneHeights[point]) > tolerance,
        _ => true,
    };
}
