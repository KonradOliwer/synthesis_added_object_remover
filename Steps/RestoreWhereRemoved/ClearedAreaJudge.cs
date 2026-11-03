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
/// centre lies within the search radius horizontally, at any height; in an interior, every such object of the interior. The share
/// standing by count and the share standing by size each have a limit; the settings say whether both or either must be at most it.
/// An object whose mesh holds only effect shapes (fog, light rays) counts in the count and adds nothing to the size. Thread-safe; each
/// area is measured once.
/// </summary>
/// <param name="contents">The cleaned mod's own objects in each column, over the shown targets where the rounds measure them, so a fitted object counts at its fitted box.</param>
/// <param name="final">The decisions with every round.</param>
/// <param name="spaces">Every space the scan read; a space not listed is measured as a worldspace.</param>
internal sealed class ClearedAreaJudge(
    AreaContents contents,
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

    /// <returns>The cleaned mod's own objects the area of the column counts, removed or not, in ascending id.</returns>
    public ImmutableArray<CountedObject> CountedIn(RestoredColumn column) => contents.In(column.Space, CentreOf(column), RadiusOf(column));

    /// <summary>The areas of the spaces one terrain quarter is judged in, added up under the verdict they give together.</summary>
    public AreaShare Sum(IReadOnlyCollection<AreaShare> areas, AreaVerdict verdict) =>
        ShareOf(areas.Sum(area => area.Objects), areas.Sum(area => area.Standing), areas.Sum(area => area.Size), areas.Sum(area => area.StandingSize), verdict);

    private bool IsInterior(RecordKey space) => spaces.TryGetValue(space, out var fact) && fact.Kind == SpaceKind.Interior;

    /// <remarks>The sums are added in ascending id, so they do not depend on which thread measures.</remarks>
    private AreaShare Measure(RecordKey space, Vector2 center, float radius)
    {
        var (objects, standing, size, standingSize) = (0, 0, 0d, 0d);
        foreach (var counted in contents.In(space, center, radius))
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

    private bool Stands(TargetId target) => final.FateOf(target) switch
    {
        BoxFate.Removed => false,
        BoxFate.Stands => true,
        var fate => throw new UnreachableException($"The cleaned mod's own object {target} has the fate {fate}; only another plugin's object goes back."),
    };

    /// <remarks>
    /// An area whose objects have no volume (only effect objects) has no size to weigh, so its count alone decides; otherwise an empty size
    /// share would pass on its own when either share is enough.
    /// </remarks>
    private AreaVerdict VerdictOf(int objects, int standing, double size, double standingSize)
    {
        if (objects == 0) return AreaVerdict.NoObjectsAround;
        var byCount = SharePercent.AtMost(standing, objects, settings.MaxStandingCountPercent);
        var cleared = size == 0 ? byCount : Combine(byCount, SharePercent.AtMost(standingSize, size, settings.MaxStandingSizePercent));
        return cleared ? AreaVerdict.Cleared : AreaVerdict.StillStanding;
    }

    private bool Combine(bool byCount, bool bySize) => settings.SharesThatMustPass switch
    {
        ClearedAreaShares.Both => byCount && bySize,
        ClearedAreaShares.Either => byCount || bySize,
        var unknown => throw new UnreachableException($"Unknown cleared-area shares {unknown}."),
    };
}
