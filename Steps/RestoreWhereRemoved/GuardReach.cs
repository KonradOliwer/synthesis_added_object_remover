using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// How far the placed guards that read the object caches look around a placed record going back, so an object beyond that cannot
/// change their answers: the too-close rule within the zones its settings grow around the record as it goes back (P1), and the test
/// of what rests on the record within the touch gap around it where it stands now and around each object found resting on it (P6).
/// Seen from above, at any height. Thread-safe.
/// </summary>
/// <param name="touchGap">The largest gap between two surfaces that the test of what rests on what counts as touching.</param>
internal sealed class GuardReach(IBaseObjectShapes shapes, TooCloseSettings tooClose, float touchGap)
{
    /// <summary>Covers the rounding slack of the boxes the caches index and the tolerances of the exact tests behind them.</summary>
    private const float Slack = 16f;

    /// <summary>
    /// Whether each object can meet a guard: within the too-close reach of a candidate as it goes back, within the touch reach of a
    /// candidate where it stands now or of an object within that, or a placed NPC, whose body the rules for NPCs measure on their own.
    /// </summary>
    /// <param name="restored">The candidates, each as it goes back.</param>
    /// <param name="now">The candidates that stand now, where they stand.</param>
    /// <returns>By the position of the object.</returns>
    public bool[] InReach(IReadOnlyList<OtherObject> objects, IEnumerable<TargetObject> restored, IEnumerable<TargetObject> now)
    {
        var tooCloseReach = new ColumnBoxes(restored.Select(candidate => (candidate.SpaceKey, TooCloseReachOf(candidate))));
        var touchReach = new ColumnBoxes(now.Select(candidate => (candidate.SpaceKey, TouchReachOf(candidate.Base, candidate.Transform))));
        if (tooCloseReach.IsEmpty && touchReach.IsEmpty) return new bool[objects.Count];
        var bounds = objects.Select(BoundsOf).ToArray();
        var restingReach = new ColumnBoxes(
            objects.Zip(bounds).Where(placed => touchReach.Overlaps(placed.First.SpaceKey, placed.Second)).Select(placed => (placed.First.SpaceKey, TouchReachOf(placed.First.Base, placed.First.Transform))));
        return
        [
            .. objects.Zip(bounds).Select(placed =>
                placed.First.IsPlacedNpc
                || tooCloseReach.Overlaps(placed.First.SpaceKey, placed.Second)
                || touchReach.Overlaps(placed.First.SpaceKey, placed.Second)
                || restingReach.Overlaps(placed.First.SpaceKey, placed.Second)),
        ];
    }

    /// <summary>The box around the record as it goes back holding every zone the too-close rule grows around it: the sphere around each zone that is on.</summary>
    public Box TooCloseReachOf(TargetObject restored)
    {
        var box = BaseBoxes.Of(shapes, restored.Base, restored.Transform);
        var radius = TooCloseZoneReach.ReachRadius(tooClose, box.HalfExtents) + Slack;
        return new Box(box.Center - new Vector3(radius), box.Center + new Vector3(radius));
    }

    /// <summary>The box the object caches index the object by, or wider.</summary>
    public Box BoundsOf(OtherObject other) => BaseBoxes.Of(shapes, other.Base, other.Transform).WorldAabb(Slack);

    private Box TouchReachOf(BaseKey? model, PlacedTransform transform) => BaseBoxes.Of(shapes, model, transform).WorldAabb(touchGap + Slack);

    /// <summary>Boxes by space, found through the exterior grid columns they cover; they meet another box when they overlap it seen from above.</summary>
    private sealed class ColumnBoxes(IEnumerable<(RecordKey Space, Box Box)> boxes)
    {
        private readonly ILookup<(RecordKey Space, int X, int Y), Box> _byColumn = boxes
            .SelectMany(entry => ColumnsOf(entry.Space, entry.Box).Select(column => (Column: column, entry.Box)))
            .ToLookup(entry => entry.Column, entry => entry.Box);

        public bool IsEmpty => _byColumn.Count == 0;

        public bool Overlaps(RecordKey space, Box box) => ColumnsOf(space, box).Any(column => _byColumn[column].Any(other => OverlapFromAbove(other, box)));

        private static IEnumerable<(RecordKey Space, int X, int Y)> ColumnsOf(RecordKey space, Box box)
        {
            var (minX, minY) = (ExteriorGrid.CellIndex(box.Min.X), ExteriorGrid.CellIndex(box.Min.Y));
            var (maxX, maxY) = (ExteriorGrid.CellIndex(box.Max.X), ExteriorGrid.CellIndex(box.Max.Y));
            for (var x = minX; x <= maxX; x++)
            {
                for (var y = minY; y <= maxY; y++) yield return (space, x, y);
            }
        }

        private bool OverlapFromAbove(Box one, Box other) =>
            one.Min.X <= other.Max.X && other.Min.X <= one.Max.X && one.Min.Y <= other.Max.Y && other.Min.Y <= one.Max.Y;
    }
}
