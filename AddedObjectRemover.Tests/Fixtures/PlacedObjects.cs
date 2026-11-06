using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>The whole collect step, both of its parts, as the run does it.</summary>
internal static class PlacedObjects
{
    public static CollectedObjects Collect(IPluginRecords plugin, ModTrustTable trust, WhatToCollect plan) =>
        Collect(plugin, trust, trust.AcceptedWorld, plan);

    public static CollectedObjects Collect(IPluginRecords plugin, ModTrustTable trust, AcceptedWorld world, WhatToCollect plan) =>
        Collect(plugin, trust, world, plan, restoredBefore: []);

    /// <param name="restoredBefore">The parts earlier runs gave back, which count in the accepted world only, as the run passes them.</param>
    public static CollectedObjects Collect(IPluginRecords plugin, ModTrustTable trust, WhatToCollect plan, ImmutableArray<RestoredPart> restoredBefore) =>
        Collect(plugin, trust, trust.AcceptedWorld, plan, restoredBefore);

    private static CollectedObjects Collect(IPluginRecords plugin, ModTrustTable trust, AcceptedWorld world, WhatToCollect plan, ImmutableArray<RestoredPart> restoredBefore) =>
        PlacedObjectCollector.ReadNonPlacedLinks(
            PlacedObjectCollector.ScanPlacedRecords(
                plugin, trust, world, plan, new(RemovalCauses.CreateOtherRecordChange(restoredBefore), RemovalCauses.CreateOtherRecordChange(restored: []))),
            plugin);
}
