namespace AddedObjectRemover;

/// <summary>
/// One of 8 equal 45° directions around a point, by the world X (east) and Y (north)
/// axes. Each spans 22.5° to either side of its compass direction; an angle exactly on a boundary
/// belongs to the direction counter-clockwise of it (22.5° from east is NorthEast). An offset with
/// no horizontal part at all counts as <see cref="CompassDirections.NoHorizontalOffset"/>.
/// </summary>
public enum DirectionSector
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
