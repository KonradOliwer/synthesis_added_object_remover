using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;

/// <summary>The changed-terrain rule judging single objects over any view of the terrain. Thread-safe.</summary>
public interface IChangedTerrainJudging
{
    /// <summary>The terrain and water of the load order as read.</summary>
    ITerrainView LoadOrder { get; }

    /// <summary>The checks over <paramref name="view"/>; each cell's versions are compared once for each set of settings asked.</summary>
    IObjectTerrainChecks Over(ITerrainView view);

    /// <summary>The objects whose check reads the land or the water of a cell of <paramref name="cells"/> with their settings.</summary>
    /// <returns>Their indexes in <paramref name="objects"/>, ascending; an object its settings do not check reads nothing.</returns>
    ImmutableArray<int> FindObjectsReading(IReadOnlyList<(TargetObject Object, ChangedTerrainSettings Settings)> objects, IReadOnlySet<ExteriorCell> cells);

    /// <summary>The cells whose land or water the object's check reads with the settings: each cell under its areas in the object's own worldspace and in the worldspace whose land it uses.</summary>
    /// <returns>None when the settings do not check the object or its space has no land.</returns>
    IReadOnlySet<ExteriorCell> FindCellsRead(TargetObject target, ChangedTerrainSettings settings);
}

/// <summary>The changed-terrain checks of single objects over one view of the terrain. Thread-safe.</summary>
public interface IObjectTerrainChecks
{
    /// <summary>Checks the object with the settings; it finds nothing when the settings could find nothing.</summary>
    TerrainCheckOutcome Check(TargetObject target, ChangedTerrainSettings settings);
}

/// <param name="Finding">Null when the check found nothing or stopped.</param>
/// <param name="Failure">Why the check stopped unexpectedly; null when it ran.</param>
public readonly record struct TerrainCheckOutcome(ObjectOnChangedTerrain? Finding, string? Failure);
