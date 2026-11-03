using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>What every plan protecting points of the same LAND records shares (<see cref="TerrainUndoPlan"/>).</summary>
internal sealed class TerrainUndoLands
{
    public TerrainUndoLands(IReadOnlyList<LandAreasJudged> lands, Func<ExteriorCell, LandHeights> readHeights, float heightTolerance, int blendDistance)
    {
        All = [.. lands];
        ByCell = lands.ToDictionary(land => land.Change.Cell);
        Candidates = lands.ToDictionary(land => land.Change.Cell, land => CandidatesOf(land.Change, land.Quarters));
        ColourCandidates = lands.ToDictionary(land => land.Change.Cell, land => ColourCandidatesOf(land.Change, land.Quarters));
        ReadHeights = readHeights;
        HeightTolerance = heightTolerance;
        BlendDistance = blendDistance;
        BlendSteps = GridStep.Within(blendDistance);
    }

    public ImmutableArray<LandAreasJudged> All { get; }

    public IReadOnlyDictionary<ExteriorCell, LandAreasJudged> ByCell { get; }

    /// <summary>Per cell, the points whose height goes back by their own record alone: undone, earlier and current heights known, every quarter holding them cleared.</summary>
    public IReadOnlyDictionary<ExteriorCell, PointSet> Candidates { get; }

    /// <summary>Per cell, the points whose colour goes back by their own record alone: undone and every quarter holding them cleared.</summary>
    public IReadOnlyDictionary<ExteriorCell, PointSet> ColourCandidates { get; }

    public Func<ExteriorCell, LandHeights> ReadHeights { get; }

    public float HeightTolerance { get; }

    /// <summary>In grid steps.</summary>
    public int BlendDistance { get; }

    /// <summary>The grid steps to the points within the blend distance, nearest first.</summary>
    public ImmutableArray<GridStep> BlendSteps { get; }

    public LandHeights HeightsOf(ExteriorCell cell) => HeightsOf(ByCell[cell].Change);

    public LandHeights HeightsOf(LandChangedByCleanedMod land) => land.Heights ?? ReadHeights(land.Cell);

    /// <param name="quarters">Empty for a LAND record that never goes back, which then has none.</param>
    private static PointSet CandidatesOf(LandChangedByCleanedMod change, ImmutableArray<LandQuarterJudged> quarters)
    {
        if (quarters.IsEmpty || change.Heights is not { Earlier: not null, Current: not null }) return PointSet.Of([]);
        return PointSet.Of(change.UndoPoints.Points.Where(point => InClearedQuarters(quarters, point)));
    }

    /// <param name="quarters">Empty for a LAND record that never goes back, which then has none.</param>
    private static PointSet ColourCandidatesOf(LandChangedByCleanedMod change, ImmutableArray<LandQuarterJudged> quarters) =>
        quarters.IsEmpty ? PointSet.Of([]) : PointSet.Of(change.UndoColours.Points.Where(point => InClearedQuarters(quarters, point)));

    /// <summary>Whether every quarter holding the point is cleared; a point on a line between quarters lies in two or four of them.</summary>
    private static bool InClearedQuarters(ImmutableArray<LandQuarterJudged> quarters, int point) =>
        TerrainQuadrants.Holding(point).All(quarter => quarters[quarter].Verdict == AreaVerdict.Cleared);
}
