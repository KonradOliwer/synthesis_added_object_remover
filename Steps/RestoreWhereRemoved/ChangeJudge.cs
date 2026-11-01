using System.Collections.Immutable;
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
        if (NeverRestored(change.WinningMod, change.LinkedFromCleanedMod, change.InDeletedCell) is { } never)
        {
            return new PlacedChangeJudged(change, never, OldPlace: null, WhereItIsNow: null, Refused: null, IsRestored: false);
        }

        var oldPlace = areas.Judge(change.SpaceKey, change.Earlier.Placement.Position);
        var whereItIsNow = change.Target is { } target ? areas.Judge(change.SpaceKey, targets[target.Index].Transform.Position) : null;
        var isRestored = oldPlace.Verdict == AreaVerdict.Cleared && whereItIsNow?.Verdict is null or AreaVerdict.Cleared;
        return new PlacedChangeJudged(change, Never: null, oldPlace, whereItIsNow, Refused: null, isRestored);
    }

    public CellChangeJudged Cell(CellCandidate cell)
    {
        var change = cell.Change;
        if (NeverRestored(change.WinningMod, change.LinkedFromCleanedMod, inDeletedCell: false) is { } never)
        {
            return new CellChangeJudged(change, cell.Undone, never, Area: null, WaterKeptBy: null, IsRestored: false);
        }

        var area = areas.Judge(change.SpaceKey, CentreOf(change));
        return new CellChangeJudged(change, cell.Undone, Never: null, area, WaterKeptBy: null, area.Verdict == AreaVerdict.Cleared);
    }

    public LandAreasJudged Land(LandChangedByCleanedMod land)
    {
        if (NeverRestored(land.WinningMod, linkedFromCleanedMod: false, land.CellDeletedInAVersion) is { } never) return new LandAreasJudged(land, never, Quarters: []);

        var spaces = spacesOnLand.GetValueOrDefault(land.Cell.WorldspaceKey, []);
        return new LandAreasJudged(
            land,
            Never: null,
            [
                .. Enumerable.Range(0, TerrainQuadrants.PerSide * TerrainQuadrants.PerSide)
                    .Select(quarter => TerrainQuadrants.CentreOf(land.Cell.X, land.Cell.Y, quarter))
                    .Select(centre => Quarter([.. spaces.Select(space => areas.Judge(space, new Vector3(centre, 0f)))])),
            ]);
    }

    /// <summary>
    /// A navmesh is judged at the centre of its bounds seen from above: its version before the cleaned mod's for a deletion, the
    /// cleaned mod's otherwise. What goes back is left to the navmesh plan.
    /// </summary>
    public NavmeshChangeJudged Navmesh(NavmeshChangedByCleanedMod navmesh)
    {
        if (NeverRestored(navmesh.WinningMod, linkedFromCleanedMod: false, inDeletedCell: false) is { } never) return new NavmeshChangeJudged(navmesh, never, Area: null, Undo: null);

        var shape = navmesh.Case is NavmeshCase.DeletedWithoutReplacement or NavmeshCase.DeletedAndReplaced ? navmesh.Earlier : navmesh.Current;
        if (shape is not { Vertices.IsEmpty: false } judged) return new NavmeshChangeJudged(navmesh, Never: null, Area: null, Undo: null);
        var (min, max) = (judged.Vertices.Aggregate(Vector3.Min), judged.Vertices.Aggregate(Vector3.Max));
        return new NavmeshChangeJudged(navmesh, Never: null, areas.Judge(navmesh.SpaceKey, Vector3.Lerp(min, max, Halfway)), Undo: null);
    }

    /// <summary>Cleared when no space says the cleaned mod's objects still stand and at least one says cleared.</summary>
    private static LandQuarterJudged Quarter(ImmutableArray<AreaShare> areas)
    {
        var verdicts = areas.Select(area => area.Verdict).ToList();
        var verdict = verdicts.Contains(AreaVerdict.StillStanding) ? AreaVerdict.StillStanding
            : verdicts.Contains(AreaVerdict.Cleared) ? AreaVerdict.Cleared
            : AreaVerdict.NoObjectsAround;
        return new LandQuarterJudged(verdict, areas);
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
