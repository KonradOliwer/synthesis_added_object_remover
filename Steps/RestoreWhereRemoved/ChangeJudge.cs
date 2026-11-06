using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>A LAND record with parts the cleaned mod is the last to change, and its quarters judged.</summary>
/// <param name="Never">Why it never goes back; null when it can.</param>
/// <param name="Quarters">The four quarters by <see cref="TerrainQuadrants.IndexOf"/>; empty when <paramref name="Never"/> is set.</param>
internal sealed record LandAreasJudged(LandChangedByCleanedMod Change, NeverRestoredReason? Never, ImmutableArray<LandQuarterJudged> Quarters);

/// <summary>Judges whether a candidate's undone parts go back: the never-rules first, then its area. Thread-safe.</summary>
/// <param name="spacesOnLand">Per worldspace holding land, the spaces holding a target whose ground it is, in key order.</param>
/// <param name="sharingAGrid">The cell records and LAND records that share their grid with another record of their kind (<see cref="SharedGrids"/>).</param>
internal sealed class ChangeJudge(
    ImmutableArray<TargetObject> targets,
    IClearedAreas areas,
    LoadOrderPlugins plugins,
    IReadOnlyDictionary<RecordKey, ImmutableArray<RecordKey>> spacesOnLand,
    CloneCandidates clones,
    IReadOnlySet<RecordKey> sharingAGrid)
{
    /// <summary>Judged points are centres: a cell's lies half a cell from its corner, bounds' halfway between their corners.</summary>
    private const float Halfway = 0.5f;

    /// <remarks>
    /// Whether an object goes back is decided by the area where it ends: its old place when its position goes back, where it stands now
    /// when a later plugin holds the position. For a moved object whose position goes back, the area where it stands now only says
    /// whether a copy is left there; an object that may not be copied goes back without one.
    /// </remarks>
    public PlacedChangeJudged Placed(RecordChangedByCleanedMod change)
    {
        var kind = KindOf(change);
        EulerPlacement? placedBack = change.Target is { } restoredTarget ? targets[restoredTarget.Index].RestoredAs(change.Earlier).Placement : null;
        if (NeverRestored(change.WinningMod, change.LinkedFromCleanedMod, change.InDeletedCell) is { } never)
        {
            return new PlacedChangeJudged(change, never, JudgedPlace: null, WhereItIsNow: null, Refused: null, IsRestored: false, kind, KeptForArea: null, placedBack);
        }

        var judgedPlace = areas.Judge(change.SpaceKey, WhereItEndsOf(change));
        var whereItIsNow = WhereItIsNowOf(change) is { } now ? areas.Judge(change.SpaceKey, now) : null;
        var judged = new PlacedChangeJudged(change, Never: null, judgedPlace, whereItIsNow, Refused: null, IsRestored: false, kind, KeptForArea: null, placedBack);
        return WithCloning(judged, CloneOf(change, judgedPlace.Verdict, whereItIsNow?.Verdict));
    }

    /// <summary>
    /// The record its area judged, with the copy rule's outcome: a record the copy rule applies to goes back, with the copy when it is
    /// planned and without one when it is refused; otherwise its area decides whether it goes back.
    /// </summary>
    /// <param name="judged">Judged by its area, with no never-rule holding; the copy rule applies only where that area is cleared.</param>
    public static PlacedChangeJudged WithCloning(PlacedChangeJudged judged, CloneJudged? cloning)
    {
        if (cloning is not null) return judged with { IsRestored = true, KeptForArea = null, Cloning = cloning };

        var keptForArea = VerdictKeeping([judged.JudgedPlace!.Verdict]);
        return judged with { IsRestored = keptForArea is null, KeptForArea = keptForArea, Cloning = null };
    }

    /// <summary>
    /// The copy rule, for another mod's object the cleaned mod moved that has no copy yet and whose old place is cleared: a copy is
    /// needed where it stands now when the cleaned mod's objects still stand in that area, or when that area does not keep it but a
    /// left-behind step measures it around one of the cleaned mod's standing objects.
    /// </summary>
    /// <param name="now">Null for a record that is no target.</param>
    /// <returns>Null when the rule does not apply.</returns>
    private CloneJudged? CloneOf(RecordChangedByCleanedMod change, AreaVerdict judgedPlace, AreaVerdict? now)
    {
        if (now is not { } verdictNow || judgedPlace !=AreaVerdict.Cleared || !CloneCandidates.IsMovedObject(change, targets) || clones.HasClone(change)) return null;

        var target = change.Target!.Value;
        var needed = verdictNow == AreaVerdict.StillStanding || clones.IsNeighbourOfStandingObject(target);
        return needed ? new CloneJudged(clones.RefusalOf(change, targets[target.Index])) : null;
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
        if ((NeverRestored(change.WinningMod, change.LinkedFromCleanedMod, inDeletedCell: false) ?? SharingAGrid(change.Cell)) is { } never)
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
        FirstPresent(verdicts, AreaVerdict.StillStanding, AreaVerdict.NoObjectsAround);

    private static AreaVerdict? FirstPresent(IReadOnlyCollection<AreaVerdict> verdicts, params AreaVerdict[] priority) =>
        priority.Cast<AreaVerdict?>().FirstOrDefault(verdict => verdicts.Contains(verdict!.Value));

    public LandAreasJudged Land(LandChangedByCleanedMod land)
    {
        if ((NeverRestored(land.WinningMod, linkedFromCleanedMod: false, land.CellDeletedInAVersion) ?? SharingAGrid(land.LandKey)) is { } never)
        {
            return new LandAreasJudged(land, never, Quarters: []);
        }

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

    private static Vector3 OldPlaceOf(RecordChangedByCleanedMod change) => change.Earlier.Placement.Position;

    /// <remarks>A position a later plugin holds stays where the winning version has it, and only the other undone parts go back there.</remarks>
    private static Vector3 WhereItEndsOf(RecordChangedByCleanedMod change) =>
        change.Earlier.Undo.HasFlag(PlacedParts.Position) ? OldPlaceOf(change) : change.WhereItIsNow?.Position ?? OldPlaceOf(change);

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
        var verdict = FirstPresent(verdicts, AreaVerdict.StillStanding, AreaVerdict.Cleared) ?? AreaVerdict.NoObjectsAround;
        return new LandQuarterJudged(areas.Sum(spaces, verdict));
    }

    /// <returns><see cref="NeverRestoredReason.SharesItsGrid"/> for a cell or LAND record sharing its grid; null otherwise.</returns>
    private NeverRestoredReason? SharingAGrid(RecordKey record) => sharingAGrid.Contains(record) ? NeverRestoredReason.SharesItsGrid : null;

    /// <param name="inDeletedCell">A version read deletes a cell the record would be written into.</param>
    /// <returns>The first never-rule that holds; null when none does.</returns>
    /// <remarks>A cell record a version deletes and a navmesh in a deleted cell are kept by their own facts and guards.</remarks>
    private NeverRestoredReason? NeverRestored(PluginName winningMod, bool linkedFromCleanedMod, bool inDeletedCell)
    {
        if (plugins.LoadsAfterPatch(winningMod)) return NeverRestoredReason.WinnerLoadsAfterPatch;
        if (linkedFromCleanedMod) return NeverRestoredReason.LinkedFromCleanedMod;
        return inDeletedCell ? NeverRestoredReason.InDeletedCell : null;
    }

    /// <remarks>An interior has no grid; any point serves, since <see cref="IClearedAreas.Judge"/> measures the whole interior wherever it is judged.</remarks>
    private static Vector3 CentreOf(CellChangedByCleanedMod cell) =>
        cell.Grid is var (x, y) ? new Vector3((x + Halfway) * ExteriorGrid.CellSize, (y + Halfway) * ExteriorGrid.CellSize, 0f) : Vector3.Zero;
}
