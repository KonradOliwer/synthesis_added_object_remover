using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// How far the placed guards that read the object caches look around a placed record going back, so an object beyond that cannot
/// change their answers: the too-close rule within the zones its settings grow around the record as it goes back, and the test of
/// what rests on what within the touch gap around the record where it stands now and as it goes back, and around each object found
/// resting on it where it stands now. Seen from above, at any height. Thread-safe.
/// </summary>
/// <param name="tooCloseSettings">The settings the too-close rule judges a record going back with.</param>
/// <param name="touchGap">The largest gap between two surfaces that the test of what rests on what counts as touching.</param>
internal sealed class GuardReach(IBaseObjectShapes shapes, ITooCloseToWhatStaysRule tooClose, TooCloseSettings tooCloseSettings, float touchGap)
{
    /// <summary>Covers the rounding slack of the boxes the caches index and the tolerances of the exact tests behind them.</summary>
    private const float Slack = 16f;

    /// <summary>
    /// Whether each object can meet a guard: within the too-close reach of a candidate as it goes back, within the touch reach of a
    /// candidate as it goes back, of one where it stands now or of an object within the latter, or a placed NPC, whose body the rules
    /// for NPCs measure on their own.
    /// </summary>
    /// <param name="restored">The candidates, each as it goes back.</param>
    /// <param name="now">The candidates that stand now, where they stand.</param>
    /// <returns>By the position of the object.</returns>
    public bool[] InReach(IReadOnlyList<OtherObject> objects, IReadOnlyList<TargetObject> restored, IEnumerable<TargetObject> now)
    {
        var tooCloseReach = new ColumnBoxes([.. restored.Select(candidate => (candidate.SpaceKey, tooClose.ZoneReachOf(candidate, tooCloseSettings)))]);
        var touchReachNow = new ColumnBoxes([.. now.Select(candidate => (candidate.SpaceKey, TouchReachOf(candidate.Base, candidate.Transform)))]);
        var touchReachGoingBack = new ColumnBoxes([.. restored.Select(candidate => (candidate.SpaceKey, TouchReachOf(candidate.Base, candidate.Transform)))]);
        if (restored.Count == 0 && touchReachNow.IsEmpty) return new bool[objects.Count];
        var bounds = objects.Select(tooClose.BoundsOf).ToArray();
        var restingReach = new ColumnBoxes(
            [.. objects.Zip(bounds).Where(placed => touchReachNow.Overlaps(placed.First.SpaceKey, placed.Second)).Select(placed => (placed.First.SpaceKey, TouchReachOf(placed.First.Base, placed.First.Transform)))]);
        return
        [
            .. objects.Zip(bounds).Select(placed =>
                placed.First.IsPlacedNpc
                || tooCloseReach.Overlaps(placed.First.SpaceKey, placed.Second)
                || touchReachGoingBack.Overlaps(placed.First.SpaceKey, placed.Second)
                || touchReachNow.Overlaps(placed.First.SpaceKey, placed.Second)
                || restingReach.Overlaps(placed.First.SpaceKey, placed.Second)),
        ];
    }

    private Box TouchReachOf(BaseKey? model, PlacedTransform transform) => BaseBoxes.Of(shapes, model, transform).WorldAabb(touchGap + Slack);

    /// <summary>Boxes by space; they meet another box when they overlap it seen from above.</summary>
    private sealed class ColumnBoxes(IReadOnlyList<(RecordKey Space, Box Box)> boxes)
    {
        private readonly BoxesByCell<RecordKey> _byCell = new(boxes);

        public bool IsEmpty => _byCell.IsEmpty;

        public bool Overlaps(RecordKey space, Box box) => _byCell.SharingACellWith(space, box).Any(position => OverlapFromAbove(_byCell.BoxAt(position), box));

        private bool OverlapFromAbove(Box one, Box other) =>
            one.Min.X <= other.Max.X && other.Min.X <= one.Max.X && one.Min.Y <= other.Max.Y && other.Min.Y <= one.Max.Y;
    }
}
