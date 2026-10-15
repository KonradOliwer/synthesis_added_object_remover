using System.Numerics;

namespace AddedObjectRemover;

public static class NearestFreePoint
{
    /// <summary>
    /// The point nearest to <paramref name="centre"/> inside the disc of <paramref name="radius"/>
    /// around it, clipped to <paramref name="clip"/>, and outside every blocked rectangle and box
    /// footprint, each grown by <paramref name="clearance"/>; null when nothing is left.
    /// </summary>
    /// <param name="blockedRectanglesWithin">The blocked rectangles that can touch the given bounds of the search area (the clipped disc).</param>
    public static Vector2? Find(
        Vector2 centre,
        float radius,
        FlatRectangle? clip,
        Func<FlatRectangle, IEnumerable<FlatRectangle>> blockedRectanglesWithin,
        IEnumerable<OrientedBox> blockedBoxes,
        float clearance)
    {
        var start = FlatArea.CreatePoint(centre);
        var searchArea = FlatArea.CreateDisc(start, radius);
        if (clip is { } clipRectangle) searchArea = FlatArea.Intersect(searchArea, FlatArea.CreateRectangle(clipRectangle));
        if (searchArea.IsEmpty) return null;

        var blocked = blockedBoxes.Select(FlatArea.CreateFootprint)
            .Concat(blockedRectanglesWithin(FlatArea.BoundsOf(searchArea)).Select(FlatArea.CreateRectangle))
            .ToList();
        var freeArea = FlatArea.Subtract(searchArea, blocked, clearance);
        return freeArea.IsEmpty ? null : FlatArea.NearestPoint(freeArea, start);
    }
}
