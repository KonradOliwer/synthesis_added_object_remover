namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The whole collect step, both of its parts, as the run does it.</summary>
internal static class PlacedObjects
{
    public static CollectedObjects Collect(IPluginRecords plugin, ModTrustTable trust, WhatToCollect plan) =>
        PlacedObjectCollector.ReadNonPlacedLinks(PlacedObjectCollector.ScanPlacedRecords(plugin, trust, plan), plugin);
}
