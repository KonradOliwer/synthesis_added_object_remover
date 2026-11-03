using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>A LAND record with parts the cleaned mod is the last to change, and its quarters judged.</summary>
/// <param name="Never">Why it never goes back; null when it can.</param>
/// <param name="Quarters">The four quarters by <see cref="TerrainQuadrants.IndexOf"/>; empty when <paramref name="Never"/> is set.</param>
internal sealed record LandAreasJudged(LandChangedByCleanedMod Change, NeverRestoredReason? Never, ImmutableArray<LandQuarterJudged> Quarters);

/// <summary>Judges whether a candidate's undone parts go back: the never-rules first, then its area. Thread-safe.</summary>
/// <param name="spacesOnLand">Per worldspace holding land, the spaces holding a target whose ground it is, in key order.</param>
internal sealed class ChangeJudge(
    ImmutableArray<TargetObject> targets, ClearedAreaJudge areas, LoadOrderPlugins plugins, IReadOnlyDictionary<RecordKey, ImmutableArray<RecordKey>> spacesOnLand)
{
    /// <summary>A cell is judged at its centre, half a cell from its corner.</summary>
    private const float HalfCell = 0.5f;

    /// <summary>The centre of bounds lies halfway between their corners.</summary>
    private const float Halfway = 0.5f;

    /// <remarks>A target the removal steps left undecided is judged both at its old place and where it stands, so a moved object is never taken out of a village that stays.</remarks>
    public PlacedChangeJudged Placed(RecordChangedByCleanedMod change)
    {
        var kind = KindOf(change);
        EulerPlacement? placedBack = change.Target is { } restoredTarget ? targets[restoredTarget.Index].RestoredAs(change.Earlier).Placement : null;
        if (NeverRestored(change.WinningMod, change.LinkedFromCleanedMod, change.InDeletedCell) is { } never)
        {
            return new PlacedChangeJudged(change, never, OldPlace: null, WhereItIsNow: null, Refused: null, IsRestored: false, kind, KeptForArea: null, placedBack);
        }

        var oldPlace = areas.Judge(change.SpaceKey, OldPlaceOf(change));
        var whereItIsNow = WhereItIsNowOf(change) is { } now ? areas.Judge(change.SpaceKey, now) : null;
        var keptForArea = VerdictKeeping(whereItIsNow is null ? [oldPlace.Verdict] : [oldPlace.Verdict, whereItIsNow.Verdict]);
        return new PlacedChangeJudged(change, Never: null, oldPlace, whereItIsNow, Refused: null, IsRestored: keptForArea is null, kind, keptForArea, placedBack);
    }

    /// <summary>What a placed record's restore puts back, by the first of its undone parts in the order of <see cref="PlacedRestoreKind"/>.</summary>
    /// <remarks>Every candidate has an undone part of one of these kinds; its navmesh door link goes back only with one of them.</remarks>
    public static PlacedRestoreKind KindOf(RecordChangedByCleanedMod change)
    {
        var undo = change.Earlier.Undo;
        if (undo.HasFlag(PlacedParts.BaseAndData)) return PlacedRestoreKind.OwnModel;
        if (undo.HasFlag(PlacedParts.Presence)) return change.PresenceBefore.Removed ? PlacedRestoreKind.HiddenAgain : PlacedRestoreKind.ShownAgain;
        if (undo.HasFlag(PlacedParts.Position)) return PlacedRestoreKind.MovedBack;
        if (undo.HasFlag(PlacedParts.Rotation) || undo.HasFlag(PlacedParts.Scale)) return PlacedRestoreKind.TurnedOrRescaledBack;
        throw new UnreachableException($"The placed record {change.Key} goes back with {undo}, which is no restored kind.");
    }

    public CellChangeJudged Cell(CellCandidate cell)
    {
        var change = cell.Change;
        if (NeverRestored(change.WinningMod, change.LinkedFromCleanedMod, inDeletedCell: false) is { } never)
        {
            return new CellChangeJudged(change, cell.Undone, never, Area: null, WaterKeptBy: null, IsRestored: false, KeptForArea: null);
        }

        var area = areas.Judge(change.SpaceKey, CentreOf(change));
        var keptForArea = VerdictKeeping([area.Verdict]);
        return new CellChangeJudged(change, cell.Undone, Never: null, area, WaterKeptBy: null, IsRestored: keptForArea is null, keptForArea);
    }

    /// <summary>
    /// Of the verdicts of the areas a change is judged in, the one that keeps it: a share that still stands before no objects around;
    /// null when every area is cleared.
    /// </summary>
    public static AreaVerdict? VerdictKeeping(IReadOnlyCollection<AreaVerdict> verdicts) =>
        verdicts.Contains(AreaVerdict.StillStanding) ? AreaVerdict.StillStanding
        : verdicts.Contains(AreaVerdict.NoObjectsAround) ? AreaVerdict.NoObjectsAround
        : null;

    public LandAreasJudged Land(LandChangedByCleanedMod land)
    {
        if (NeverRestored(land.WinningMod, linkedFromCleanedMod: false, land.CellDeletedInAVersion) is { } never) return new LandAreasJudged(land, never, Quarters: []);

        var spaces = SpacesOn(land);
        return new LandAreasJudged(
            land,
            Never: null,
            [.. Enumerable.Range(0, TerrainQuadrants.Count).Select(quarter => Quarter([.. spaces.Select(space => areas.Judge(space, QuarterCentreOf(land, quarter)))]))]);
    }

    /// <summary>
    /// A navmesh is judged at the centre of its bounds seen from above: its version before the cleaned mod's for a deletion, the
    /// cleaned mod's otherwise. What goes back is left to the navmesh plan.
    /// </summary>
    /// <remarks>Its undo is left to the navmesh plan, which also says when nothing in a navmesh whose area is cleared goes back.</remarks>
    public NavmeshChangeJudged Navmesh(NavmeshChangedByCleanedMod navmesh)
    {
        if (NeverRestored(navmesh.WinningMod, linkedFromCleanedMod: false, inDeletedCell: false) is { } never)
        {
            return new NavmeshChangeJudged(navmesh, never, Area: null, Undo: null, KeptForArea: null, NothingGoesBack: false);
        }

        if (JudgedPointOf(navmesh) is not { } point) return new NavmeshChangeJudged(navmesh, Never: null, Area: null, Undo: null, KeptForArea: null, NothingGoesBack: true);
        var area = areas.Judge(navmesh.SpaceKey, point);
        return new NavmeshChangeJudged(navmesh, Never: null, area, Undo: null, VerdictKeeping([area.Verdict]), NothingGoesBack: false);
    }

    /// <summary>The points a placed record whose areas let it go back was judged at; none for any other.</summary>
    public IEnumerable<(RecordKey Space, Vector3 Point)> ClearedPoints(PlacedChangeJudged judged)
    {
        if (!judged.IsRestored) yield break;
        var change = judged.Change;
        yield return (change.SpaceKey, OldPlaceOf(change));
        if (WhereItIsNowOf(change) is { } now) yield return (change.SpaceKey, now);
    }

    /// <summary>The point a cell record whose area lets it go back was judged at; none for any other.</summary>
    public IEnumerable<(RecordKey Space, Vector3 Point)> ClearedPoints(CellChangeJudged judged) =>
        judged.IsRestored ? [(judged.Change.SpaceKey, CentreOf(judged.Change))] : [];

    /// <summary>The centres of the cleared quarters of a LAND record, in each space using its land.</summary>
    public IEnumerable<(RecordKey Space, Vector3 Point)> ClearedPoints(LandAreasJudged judged) =>
        Enumerable.Range(0, judged.Quarters.Length)
            .Where(quarter => judged.Quarters[quarter].Verdict == AreaVerdict.Cleared)
            .SelectMany(quarter => SpacesOn(judged.Change).Select(space => (space, QuarterCentreOf(judged.Change, quarter))));

    /// <summary>The point a navmesh whose area is cleared was judged at; none for any other.</summary>
    public IEnumerable<(RecordKey Space, Vector3 Point)> ClearedPoints(NavmeshChangeJudged judged) =>
        judged is { Never: null, Area: not null, KeptForArea: null } && JudgedPointOf(judged.Change) is { } point ? [(judged.Change.SpaceKey, point)] : [];

    private static Vector3 OldPlaceOf(RecordChangedByCleanedMod change) => change.Earlier.Placement.Position;

    /// <returns>Null for a record that is no target.</returns>
    private Vector3? WhereItIsNowOf(RecordChangedByCleanedMod change) => change.Target is { } target ? targets[target.Index].Transform.Position : null;

    private ImmutableArray<RecordKey> SpacesOn(LandChangedByCleanedMod land) => spacesOnLand.GetValueOrDefault(land.Cell.WorldspaceKey, []);

    private static Vector3 QuarterCentreOf(LandChangedByCleanedMod land, int quarter) => new(TerrainQuadrants.CentreOf(land.Cell.X, land.Cell.Y, quarter), 0f);

    /// <returns>The centre of the navmesh's judged version's bounds; null when that version has no vertex.</returns>
    private static Vector3? JudgedPointOf(NavmeshChangedByCleanedMod navmesh)
    {
        var shape = navmesh.Case == NavmeshCase.Deleted ? navmesh.Earlier : navmesh.Current;
        if (shape is not { Vertices.IsEmpty: false } judged) return null;
        return Vector3.Lerp(judged.Vertices.Aggregate(Vector3.Min), judged.Vertices.Aggregate(Vector3.Max), Halfway);
    }

    /// <summary>Cleared when no space says the cleaned mod's objects still stand and at least one says cleared.</summary>
    private LandQuarterJudged Quarter(ImmutableArray<AreaShare> spaces)
    {
        var verdicts = spaces.Select(area => area.Verdict).ToList();
        var verdict = verdicts.Contains(AreaVerdict.StillStanding) ? AreaVerdict.StillStanding
            : verdicts.Contains(AreaVerdict.Cleared) ? AreaVerdict.Cleared
            : AreaVerdict.NoObjectsAround;
        return new LandQuarterJudged(areas.Sum(spaces, verdict));
    }

    /// <param name="inDeletedCell">A version read deletes a cell the record would be written into.</param>
    /// <returns>The first never-rule that holds; null when none does.</returns>
    /// <remarks>A cell record a version deletes and a navmesh in a deleted cell are kept by their own facts and guards.</remarks>
    private NeverRestoredReason? NeverRestored(PluginName winningMod, bool linkedFromCleanedMod, bool inDeletedCell)
    {
        if (plugins.LoadsAfterPatch(winningMod)) return NeverRestoredReason.WinnerLoadsAfterPatch;
        if (linkedFromCleanedMod) return NeverRestoredReason.LinkedFromCleanedMod;
        return inDeletedCell ? NeverRestoredReason.InDeletedCell : null;
    }

    /// <remarks>An interior has no grid; any point serves, since <see cref="ClearedAreaJudge.Judge"/> measures the whole interior wherever it is judged.</remarks>
    private static Vector3 CentreOf(CellChangedByCleanedMod cell) =>
        cell.Grid is var (x, y) ? new Vector3((x + HalfCell) * ExteriorGrid.CellSize, (y + HalfCell) * ExteriorGrid.CellSize, 0f) : Vector3.Zero;
}
