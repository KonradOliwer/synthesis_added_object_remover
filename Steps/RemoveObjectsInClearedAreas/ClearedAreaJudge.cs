using System.Diagnostics;
using System.Numerics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsInClearedAreas;

/// <summary>
/// Judges how much of the cleaned mod's own objects the game draws still stand in the area around a point: the objects whose box
/// centre lies within the search radius horizontally, at any height; in an interior, every such object of the interior. The share
/// standing by count and the share standing by size each have a limit; both must be at most it.
/// An object whose mesh holds only effect shapes (fog, light rays) counts in the count and adds nothing to the size. Thread-safe; each
/// area is measured once.
/// </summary>
/// <param name="contents">The cleaned mod's own objects in each column, over the shown targets where the rounds measure them, so a fitted object counts at its fitted box.</param>
/// <param name="decisions">The decisions the areas are judged on.</param>
/// <param name="spaces">Every space the scan read; a space not listed is measured as a worldspace.</param>
internal sealed class ClearedAreaJudge(
    AreaContents contents,
    IRemovalDecisions decisions,
    IReadOnlyDictionary<RecordKey, SpaceFact> spaces,
    ClearedArea settings) : IClearedAreas
{
    private readonly ComputedOncePerKey<Column, AreaShare> _shares = new(Publication.FirstWriteWins, EqualityComparer<Column>.Default);

    public AreaShare Judge(RecordKey space, Vector3 point) => ShareIn(ColumnAt(space, point));

    /// <returns>The share standing in the area around the target, and the objects that share counts with the target itself left out.</returns>
    public (AreaShare Area, int ObjectsAround) JudgeAround(TargetObject target)
    {
        var column = ColumnAt(target.SpaceKey, target.Transform.Position);
        var area = ShareIn(column);
        return (area, contents.Counts(target.Id, column.Centre, column.Radius) ? area.Objects - 1 : area.Objects);
    }

    public AreaShare Sum(IReadOnlyCollection<AreaShare> areas, AreaVerdict verdict) =>
        ShareOf(areas.Sum(area => area.Objects), areas.Sum(area => area.Standing), areas.Sum(area => area.Size), areas.Sum(area => area.StandingSize), verdict);

    /// <remarks>An interior is one area: an endless column at one fixed point holds all of it.</remarks>
    private Column ColumnAt(RecordKey space, Vector3 point) =>
        IsInterior(space) ? new Column(space, Vector2.Zero, float.PositiveInfinity) : new Column(space, new Vector2(point.X, point.Y), settings.SearchRadius);

    private AreaShare ShareIn(Column column) => _shares.Get(column, () => Measure(column));

    private bool IsInterior(RecordKey space) => spaces.TryGetValue(space, out var fact) && fact.Kind == SpaceKind.Interior;

    /// <remarks>The sums are added in ascending id, so they do not depend on which thread measures.</remarks>
    private AreaShare Measure(Column column)
    {
        var (objects, standing, size, standingSize) = (0, 0, 0d, 0d);
        foreach (var counted in contents.In(column.Space, column.Centre, column.Radius))
        {
            objects++;
            size += counted.Volume;
            if (!Stands(counted.Target)) continue;
            standing++;
            standingSize += counted.Volume;
        }
        return ShareOf(objects, standing, size, standingSize, VerdictOf(objects, standing, size, standingSize));
    }

    private static AreaShare ShareOf(int objects, int standing, double size, double standingSize, AreaVerdict verdict) =>
        new(objects, standing, size, standingSize, verdict, SharePercent.Of(standing, objects), SharePercent.Of(standingSize, size));

    private bool Stands(TargetId target) => decisions.FateOf(target) switch
    {
        BoxFate.Removed => false,
        BoxFate.Stands => true,
        var fate => throw new UnreachableException($"The cleaned mod's own object {target} has the fate {fate}; only another plugin's object goes back."),
    };

    /// <remarks>
    /// An area whose objects have no volume (only effect objects) has no size to weigh, so its count alone decides.
    /// </remarks>
    private AreaVerdict VerdictOf(int objects, int standing, double size, double standingSize)
    {
        if (objects == 0) return AreaVerdict.NoObjectsAround;
        var byCount = SharePercent.AtMost(standing, objects, settings.MaxStandingCountPercent);
        var cleared = byCount && (size == 0 || SharePercent.AtMost(standingSize, size, settings.MaxStandingSizePercent));
        return cleared ? AreaVerdict.Cleared : AreaVerdict.StillStanding;
    }

    /// <summary>Where an area is measured: every object whose box centre lies within the radius of the centre horizontally.</summary>
    private readonly record struct Column(RecordKey Space, Vector2 Centre, float Radius);
}
