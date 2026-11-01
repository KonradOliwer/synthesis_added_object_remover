using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// Whether the LAND records holding a grid point (two or four on a cell edge) agree on it: on its earlier height, on the steps
/// they store heights on, and on letting its colour go back.
/// </summary>
internal sealed class TerrainEdgeChecks(TerrainUndoLands lands)
{
    /// <summary>Two records whose offsets lie closer than this to a whole number of steps apart store heights on the same steps.</summary>
    private const float SameStepsTolerance = 1e-3f;

    private readonly TerrainUndoLands _lands = lands;

    /// <summary>Whether every record holding the point's world grid point lets its colour go back there.</summary>
    public bool ColourGoesBack(LandGridPoint point)
    {
        foreach (var holder in WorldGridPoint.Of(point).Holders())
        {
            if (!_lands.ColourCandidates.TryGetValue(holder.Cell, out var colours) || !colours.Contains(holder.Point)) return false;
        }
        return true;
    }

    public bool EarlierHeightsAgree(LandGridPointHolders holders)
    {
        var first = EarlierHeightAt(holders[0]);
        foreach (var holder in holders)
        {
            if (MathF.Abs(EarlierHeightAt(holder) - first) > _lands.HeightTolerance) return false;
        }
        return true;
    }

    /// <summary>Whether the records' winning heights lie whole steps apart, so a height on one record's steps lies on every one's.</summary>
    public bool StepsAgree(LandGridPointHolders holders)
    {
        var first = _lands.HeightsOf(holders[0].Cell).Current![0];
        foreach (var holder in holders)
        {
            var steps = (_lands.HeightsOf(holder.Cell).Current![0] - first) / TerrainGrid.UnitsPerHeightStep;
            if (MathF.Abs(steps - MathF.Floor(steps + 0.5f)) > SameStepsTolerance) return false;
        }
        return true;
    }

    private float EarlierHeightAt(LandGridPoint point) => _lands.HeightsOf(point.Cell).Earlier![point.Point];
}
