using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// Which grid points go back by a plan's protection alone, and why a point the cleaned mod last changed in a cleared quarter
/// does not. A point goes back when every LAND record holding it lets it: the cleaned mod is its last changer there, every quarter
/// holding it is cleared, nothing protects it, and the records held equal earlier heights and store heights on the same steps.
/// </summary>
internal sealed class TerrainPointSelection(TerrainUndoLands lands, ImmutableDictionary<LandGridPoint, TerrainKeptReason> protectedPoints, TerrainEdgeChecks edges)
{
    private readonly TerrainUndoLands _lands = lands;

    /// <summary>Each protected point with the first reason it was protected for.</summary>
    private readonly ImmutableDictionary<LandGridPoint, TerrainKeptReason> _protected = protectedPoints;

    private readonly TerrainEdgeChecks _edges = edges;

    /// <summary>Whether every record holding the point lets it go back by the protection alone.</summary>
    public bool GoesBack(WorldGridPoint world)
    {
        var holders = world.Holders();
        foreach (var holder in holders)
        {
            if (!IsFree(holder)) return false;
        }
        return _edges.EarlierHeightsAgree(holders) && _edges.StepsAgree(holders);
    }

    /// <summary>
    /// The points the cleaned mod last changed that a cleared quarter holds and that do not go back: a candidate by its reason, any
    /// other point because another quarter holding it is not cleared or a version holds no height map.
    /// </summary>
    /// <param name="tooSteep">The cells found to keep every height, since their heights with any restored could not be stored.</param>
    public ImmutableArray<LandPointsKept> KeptPoints(LandAreasJudged land, PointSet candidates, PointSet restored, IReadOnlySet<ExteriorCell> tooSteep) =>
    [
        .. land.Change.UndoPoints.Points
            .Where(point => !restored.Contains(point) && TerrainQuadrants.Holding(point).Any(quarter => land.Quarters[quarter].Verdict == AreaVerdict.Cleared))
            .GroupBy(point => candidates.Contains(point) ? KeptReasonOf(new LandGridPoint(land.Change.Cell, point), tooSteep) : ReasonNoCandidateIsKept(land.Change))
            .OrderBy(group => group.Key)
            .Select(group => new LandPointsKept(group.Key, PointSet.Of(group))),
    ];

    /// <summary>Why a point in a cleared quarter is no candidate (<see cref="TerrainUndoLands.Candidates"/>).</summary>
    private static TerrainKeptReason ReasonNoCandidateIsKept(LandChangedByCleanedMod change) =>
        change.Heights is { Earlier: not null, Current: not null } ? TerrainKeptReason.NextToQuarterThatStays : TerrainKeptReason.WithoutHeightMap;

    /// <summary>
    /// The point's own protection first, then a protection of another record holding it, then a cell too steep to store, then a
    /// record holding it that does not let it go back, then the records' earlier heights; a point none of these keeps is kept by
    /// its records' steps, the only other reason a point that every record frees does not go back.
    /// </summary>
    private TerrainKeptReason KeptReasonOf(LandGridPoint point, IReadOnlySet<ExteriorCell> tooSteep)
    {
        var holders = WorldGridPoint.Of(point).Holders().OrderBy(holder => holder != point).ToList();
        foreach (var holder in holders)
        {
            if (_protected.TryGetValue(holder, out var reason)) return reason;
        }
        if (holders.Any(holder => tooSteep.Contains(holder.Cell))) return TerrainKeptReason.TooSteepToStore;
        if (!holders.All(IsFree)) return TerrainKeptReason.OnEdgeOfLandThatStays;
        return _edges.EarlierHeightsAgree(WorldGridPoint.Of(point).Holders()) ? TerrainKeptReason.EdgeStoredOnDifferentSteps : TerrainKeptReason.EarlierEdgeHeightsDiffer;
    }

    private bool IsFree(LandGridPoint point) =>
        _lands.Candidates.TryGetValue(point.Cell, out var candidates) && candidates.Contains(point.Point) && !_protected.ContainsKey(point);
}
