using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind;

/// <summary>
/// Judges how much of the cleaned mod's own objects the game draws still stand in the area around a point: the objects whose box
/// centre lies within the search radius horizontally, at any height; in an interior, every such object of the interior. The share
/// standing by count and the share standing by size each have a limit; both must be at most it.
/// An object whose mesh holds only effect shapes (fog, light rays) counts in the count and adds nothing to the size. One of the cleaned
/// mod's objects a later plugin holds, when the contents count them, always stands. Thread-safe; each
/// area is measured once.
/// </summary>
internal sealed class ClearedAreaJudge : IClearedAreas
{
    private readonly ComputedOncePerKey<Column, AreaShare> _shares = new(Publication.FirstWriteWins, EqualityComparer<Column>.Default);
    private readonly AreaContents _contents;
    private readonly IRemovalDecisions _decisions;
    private readonly IReadOnlyDictionary<RecordKey, SpaceFact> _spaces;
    private readonly ClearedArea _settings;

    /// <summary>The cleaned mod's objects counted as removed although the decisions leave them standing.</summary>
    private readonly ImmutableHashSet<TargetId> _alsoRemoved;

    /// <summary>The shares of the columns known before the newest of <see cref="_alsoRemoved"/> were counted that hold none of those.</summary>
    private readonly IReadOnlyDictionary<Column, AreaShare> _unchanged;

    /// <param name="contents">The cleaned mod's own objects in each column, over the shown targets where the rounds measure them, so a fitted object counts at its fitted box.</param>
    /// <param name="decisions">The decisions the areas are judged on.</param>
    /// <param name="spaces">Every space the scan read; a space not listed is measured as a worldspace.</param>
    public ClearedAreaJudge(AreaContents contents, IRemovalDecisions decisions, IReadOnlyDictionary<RecordKey, SpaceFact> spaces, ClearedArea settings)
        : this(contents, decisions, spaces, settings, [], new Dictionary<Column, AreaShare>())
    {
    }

    private ClearedAreaJudge(
        AreaContents contents,
        IRemovalDecisions decisions,
        IReadOnlyDictionary<RecordKey, SpaceFact> spaces,
        ClearedArea settings,
        ImmutableHashSet<TargetId> alsoRemoved,
        IReadOnlyDictionary<Column, AreaShare> unchanged)
    {
        (_contents, _decisions, _spaces, _settings, _alsoRemoved, _unchanged) = (contents, decisions, spaces, settings, alsoRemoved, unchanged);
    }

    public AreaShare Judge(RecordKey space, Vector3 point) => ShareIn(ColumnAt(space, point));

    public (AreaShare Area, int ObjectsAround) JudgeAround(TargetObject target)
    {
        var column = ColumnAt(target.SpaceKey, target.Transform.Position);
        var area = ShareIn(column);
        return (area, Holds(column, target.Id) ? area.Objects - 1 : area.Objects);
    }

    public AreaShare Sum(IReadOnlyCollection<AreaShare> areas, AreaVerdict verdict) =>
        ShareOf(areas.Sum(area => area.Objects), areas.Sum(area => area.Standing), areas.Sum(area => area.Size), areas.Sum(area => area.StandingSize), verdict);

    /// <remarks>Only another plugin's object can go back, and the contents never count one, so its fate is never asked.</remarks>
    public List<TargetId> StandingWhereAnAreaStillStands(IReadOnlyList<TargetId> targets)
    {
        List<Column> stillStanding = [.. _shares.Entries().Where(entry => entry.Value.Verdict == AreaVerdict.StillStanding).Select(entry => entry.Key)];
        return [.. targets.Where(target => stillStanding.Any(column => Holds(column, target)) && Stands(target))];
    }

    public IClearedAreas AlsoRemoving(IReadOnlyCollection<TargetId> removed)
    {
        if (removed.Where(target => !Stands(target)).Select(target => (TargetId?)target).FirstOrDefault() is { } notStanding)
        {
            throw new UnreachableException($"The areas were asked to count target {notStanding} as removed, which they count as removed already.");
        }
        var unchanged = _shares.Entries().Where(entry => !removed.Any(target => Holds(entry.Key, target))).ToDictionary(entry => entry.Key, entry => entry.Value);
        return new ClearedAreaJudge(_contents, _decisions, _spaces, _settings, _alsoRemoved.Union(removed), unchanged);
    }

    private bool Holds(Column column, TargetId target) => _contents.Counts(target, column.Space, column.Centre, column.Radius);

    /// <remarks>An interior is one area: an endless column at one fixed point holds all of it.</remarks>
    private Column ColumnAt(RecordKey space, Vector3 point) =>
        IsInterior(space) ? new Column(space, Vector2.Zero, float.PositiveInfinity) : new Column(space, new Vector2(point.X, point.Y), _settings.SearchRadius);

    /// <remarks>A column that holds none of the objects counted last stands as it did before them, so its share is the earlier one.</remarks>
    private AreaShare ShareIn(Column column) => _shares.Get(column, () => _unchanged.TryGetValue(column, out var share) ? share : Measure(column));

    private bool IsInterior(RecordKey space) => _spaces.TryGetValue(space, out var fact) && fact.Kind == SpaceKind.Interior;

    /// <remarks>The sums are added in ascending id, so they do not depend on which thread measures.</remarks>
    private AreaShare Measure(Column column)
    {
        var (objects, standing, size, standingSize) = (0, 0, 0d, 0d);
        foreach (var counted in _contents.In(column.Space, column.Centre, column.Radius))
        {
            objects++;
            size += counted.Volume;
            if (!counted.IsOverriddenLater && !Stands(counted.Target)) continue;
            standing++;
            standingSize += counted.Volume;
        }
        return ShareOf(objects, standing, size, standingSize, VerdictOf(objects, standing, size, standingSize));
    }

    private static AreaShare ShareOf(int objects, int standing, double size, double standingSize, AreaVerdict verdict) =>
        new(objects, standing, size, standingSize, verdict, SharePercent.Of(standing, objects), SharePercent.Of(standingSize, size));

    private bool Stands(TargetId target) => !_alsoRemoved.Contains(target) && _decisions.FateOf(target) switch
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
        var byCount = SharePercent.AtMost(standing, objects, _settings.MaxStandingCountPercent);
        var cleared = byCount && (size == 0 || SharePercent.AtMost(standingSize, size, _settings.MaxStandingSizePercent));
        return cleared ? AreaVerdict.Cleared : AreaVerdict.StillStanding;
    }

    /// <summary>Where an area is measured: every object whose box centre lies within the radius of the centre horizontally.</summary>
    private readonly record struct Column(RecordKey Space, Vector2 Centre, float Radius);
}
