namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The whole collect step, both of its parts, as the run does it.</summary>
internal static class PlacedObjects
{
    public static CollectedObjects Collect(IPluginRecords plugin, ModTrustTable trust, WhatToCollect plan) =>
        Collect(plugin, trust, trust.AcceptedWorld, plan);

    public static CollectedObjects Collect(IPluginRecords plugin, ModTrustTable trust, AcceptedWorld world, WhatToCollect plan) =>
        PlacedObjectCollector.ReadNonPlacedLinks(PlacedObjectCollector.ScanPlacedRecords(plugin, trust, world, plan), plugin);
}
