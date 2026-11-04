using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;

namespace AddedObjectRemover;

/// <summary>Builds what the scan collects.</summary>
internal static class WhatToCollectBuilder
{
    /// <summary>What the scan collects is what the steps that are on need read; each step's settings state their own need.</summary>
    /// <param name="reports">Null unless the detailed log and the report files are on.</param>
    public static WhatToCollect Create(
        MarkerMoveSettings markerMoves,
        ReportFileSettings? reports,
        ChangedTerrainSettings cleanedModTerrain,
        ChangedTerrainSettings changedObjectsTerrain,
        ChangedObjectSettings changedObjects,
        bool processReplaced,
        float movedDistance,
        WhereRemovedRestoreSettings restoreWhereRemoved) =>
        new(
            ScanNeeds.UnionOf(
                markerMoves.Needs,
                reports?.Needs,
                ChangedTerrainScanNeeds.OfBoth(cleanedModTerrain, changedObjectsTerrain),
                changedObjects.NeedsWhenReplacedObjectsAre(processReplaced),
                restoreWhereRemoved.Needs),
            movedDistance,
            processReplaced);
}
