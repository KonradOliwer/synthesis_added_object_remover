using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>What became of a LAND record once its plan is final: in each quarter holding a change, and for the rest of the record.</summary>
internal static class LandOutcomes
{
    /// <summary>A LAND record a never-rule keeps whole.</summary>
    public static LandChangeJudged NeverRestored(LandChangedByCleanedMod change, NeverRestoredReason never)
    {
        var nothing = PointSet.Of([]);
        return new LandChangeJudged(
            change,
            never,
            [],
            nothing,
            nothing,
            default,
            Rest: false,
            [],
            FinalHeights: null,
            CellEdgeHeights.Unknown,
            IsRestored: false,
            ChangedQuarters(change, quarters: [], nothing, nothing, default, []),
            RestKeptForArea: null);
    }

    /// <summary>A LAND record no never-rule keeps, with what of it goes back.</summary>
    /// <param name="quarters">The four quarters by <see cref="TerrainQuadrants.IndexOf"/>.</param>
    /// <param name="rest">Whether the rest of the record goes back.</param>
    public static LandChangeJudged Judged(
        LandChangedByCleanedMod change,
        ImmutableArray<LandQuarterJudged> quarters,
        PointSet heights,
        PointSet colours,
        QuarterSet textures,
        bool rest,
        ImmutableArray<LandPointsKept> kept,
        float[]? finalHeights,
        CellEdgeHeights beyond) =>
        new(
            change,
            Never: null,
            quarters,
            heights,
            colours,
            textures,
            rest,
            kept,
            finalHeights,
            beyond,
            IsRestored: !heights.IsEmpty || !colours.IsEmpty || !textures.IsEmpty || rest,
            ChangedQuarters(change, quarters, heights, colours, textures, kept),
            change.UndoRest && !rest ? ChangeJudge.VerdictKeeping([.. quarters.Select(quarter => quarter.Verdict)]) : null);

    /// <param name="quarters">Empty when a never-rule keeps the land.</param>
    private static ImmutableArray<LandQuarterOutcome> ChangedQuarters(
        LandChangedByCleanedMod change, ImmutableArray<LandQuarterJudged> quarters, PointSet heights, PointSet colours, QuarterSet textures, ImmutableArray<LandPointsKept> kept)
    {
        var changedHeights = TerrainQuadrants.CountPerQuadrant(change.UndoPoints.Points);
        var changedColours = TerrainQuadrants.CountPerQuadrant(change.UndoColours.Points);
        var restoredHeights = TerrainQuadrants.CountPerQuadrant(heights.Points);
        var restoredColours = TerrainQuadrants.CountPerQuadrant(colours.Points);
        var laterHeights = TerrainQuadrants.CountPerQuadrant(change.LeftToLater.Points.Points);
        var laterColours = TerrainQuadrants.CountPerQuadrant(change.LeftToLater.Colours.Points);
        var keptPerQuarter = kept.Select(entry => (entry.Reason, Counts: TerrainQuadrants.CountPerQuadrant(entry.Points.Points))).ToList();
        return
        [
            .. Enumerable.Range(0, TerrainQuadrants.Count)
                .Select(quarter => (
                    Quarter: quarter,
                    Judged: quarters.IsEmpty ? null : quarters[quarter],
                    Changed: new TerrainPieces(changedHeights[quarter], changedColours[quarter], change.UndoQuarters.Contains(quarter)),
                    Restored: new TerrainPieces(restoredHeights[quarter], restoredColours[quarter], textures.Contains(quarter)),
                    Later: new TerrainPieces(laterHeights[quarter], laterColours[quarter], change.LeftToLater.Quarters.Contains(quarter))))
                .Where(facts => facts.Changed.Any)
                .Select(facts =>
                {
                    ImmutableArray<QuarterPointsKept> keptHere = facts.Judged is { Verdict: AreaVerdict.Cleared }
                        ? [.. keptPerQuarter.Where(entry => entry.Counts[facts.Quarter] > 0).Select(entry => new QuarterPointsKept(entry.Reason, entry.Counts[facts.Quarter]))]
                        : [];
                    var nothingGoesBack = !facts.Restored.Any;
                    return new LandQuarterOutcome(
                        facts.Quarter,
                        facts.Judged?.Area,
                        facts.Changed,
                        facts.Restored,
                        facts.Later,
                        keptHere,
                        nothingGoesBack && facts.Judged is { Verdict: not AreaVerdict.Cleared } judged ? judged.Verdict : null,
                        nothingGoesBack && facts.Judged is { Verdict: AreaVerdict.Cleared } && keptHere.IsEmpty);
                }),
        ];
    }
}
