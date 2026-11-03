using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// Judges how much of the cleaned mod's own objects the game draws still stand in the area around a point: the objects whose box
/// centre lies within the search radius horizontally, at any height; in an interior, every such object of the interior. An object
/// whose mesh holds only effect shapes (water spray, a waterfall's skirt) counts by the box of those shapes. Thread-safe; each area is
/// measured once.
/// </summary>
/// <param name="targets">Every target, by id; only the cleaned mod's own objects count.</param>
/// <param name="shownTargets">The shown targets where the rounds measure them, so a fitted object counts at its fitted box.</param>
/// <param name="final">The decisions with every round.</param>
/// <param name="spaces">Every space the scan read; a space not listed is measured as a worldspace.</param>
internal sealed class ClearedAreaJudge(
    ImmutableArray<TargetObject> targets,
    IVisibleTargetObjects shownTargets,
    IRemovalDecisions final,
    IReadOnlyDictionary<RecordKey, SpaceFact> spaces,
    ClearedArea settings)
{
    private readonly ComputedOncePerKey<(RecordKey Space, Vector2 Center), AreaShare> _shares = new(Publication.FirstWriteWins, EqualityComparer<(RecordKey, Vector2)>.Default);

    public AreaShare Judge(RecordKey space, Vector3 point)
    {
        var column = ColumnAt(space, point);
        var center = CentreOf(column);
        return _shares.Get((space, center), () => Measure(space, center, RadiusOf(column)));
    }

    /// <summary>The column the area around the point is measured in: its centre seen from above; none in an interior, which is one area.</summary>
    public RestoredColumn ColumnAt(RecordKey space, Vector3 point) => new(space, IsInterior(space) ? null : new Vector2(point.X, point.Y));

    /// <remarks>An interior is one area: an endless column at any point holds all of it.</remarks>
    public float RadiusOf(RestoredColumn column) => column.Centre is null ? float.PositiveInfinity : settings.SearchRadius;

    public static Vector2 CentreOf(RestoredColumn column) => column.Centre ?? Vector2.Zero;

    /// <summary>The areas of the spaces one terrain quarter is judged in, added up under the verdict they give together.</summary>
    public AreaShare Sum(IReadOnlyCollection<AreaShare> areas, AreaVerdict verdict) =>
        ShareOf(areas.Sum(area => area.Objects), areas.Sum(area => area.Standing), areas.Sum(area => area.Size), areas.Sum(area => area.StandingSize), verdict);

    private bool IsInterior(RecordKey space) => spaces.TryGetValue(space, out var fact) && fact.Kind == SpaceKind.Interior;

    /// <remarks>The sums are added in ascending id, so they do not depend on which thread measures.</remarks>
    private AreaShare Measure(RecordKey space, Vector2 center, float radius)
    {
        var inColumn = new List<VisibleTargetNear>();
        shownTargets.InColumn(space, center, radius, inColumn);
        var (objects, standing, size, standingSize) = (0, 0, 0d, 0d);
        foreach (var counted in inColumn.Where(near => targets[near.Target.Index].Origin == TargetOrigin.CleanedMod))
        {
            var volume = (double)counted.Box.Volume;
            objects++;
            size += volume;
            if (!Stands(counted.Target)) continue;
            standing++;
            standingSize += volume;
        }
        return ShareOf(objects, standing, size, standingSize, VerdictOf(objects, standing, size, standingSize));
    }

    private AreaShare ShareOf(int objects, int standing, double size, double standingSize, AreaVerdict verdict)
    {
        var measure = MeasureFor(size);
        var (standingWeight, weight) = measure == ClearedAreaMeasure.ObjectSize ? (standingSize, size) : (standing, (double)objects);
        var percent = weight > 0 ? standingWeight / weight * Percent.PerWhole : 0;
        return new AreaShare(objects, standing, size, standingSize, verdict, measure, percent);
    }

    /// <remarks>When no object counted has a volume, the share is taken by count.</remarks>
    private ClearedAreaMeasure MeasureFor(double size) =>
        settings.Measure == ClearedAreaMeasure.ObjectSize && size > 0 ? ClearedAreaMeasure.ObjectSize : ClearedAreaMeasure.ObjectCount;

    private bool Stands(TargetId target) => final.FateOf(target) switch
    {
        BoxFate.Removed => false,
        BoxFate.Stands => true,
        var fate => throw new UnreachableException($"The cleaned mod's own object {target} has the fate {fate}; only another plugin's object goes back."),
    };

    private AreaVerdict VerdictOf(int objects, int standing, double size, double standingSize)
    {
        if (objects == 0) return AreaVerdict.NoObjectsAround;
        var cleared = MeasureFor(size) == ClearedAreaMeasure.ObjectSize
            ? SharePercent.AtMost(standingSize, size, settings.MaxStandingPercent)
            : SharePercent.AtMost(standing, objects, settings.MaxStandingPercent);
        return cleared ? AreaVerdict.Cleared : AreaVerdict.StillStanding;
    }
}
