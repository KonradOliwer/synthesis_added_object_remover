using Noggog;

namespace AddedObjectRemover;

/// <summary>Reads the coordinates a navmesh parent or navigation map info entry stores for an exterior cell.</summary>
internal static class NavmeshParentCoordinates
{
    /// <remarks>The game stores these coordinates Y first: Mutagen's X holds the cell's grid Y and its Y holds the grid X.</remarks>
    public static CellGridPoint GridPointOf(P2Int16 stored) => new(stored.Y, stored.X);
}
