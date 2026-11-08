using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals;

/// <summary>Creates the rules of which other mods' objects can cause removals.</summary>
public static class RemovalCauses
{
    /// <summary>Whether another mod's placed record is changed.</summary>
    /// <param name="restored">The parts earlier runs gave back, which the patch's version holds as accepted while it keeps their values.</param>
    public static IOtherRecordChange CreateOtherRecordChange(ImmutableArray<RestoredPart> restored) => new OtherRecordChange(new RestoredPartsByRecord(restored));

    /// <summary>The change rules of the run's world, which counts the parts earlier runs gave back, and of the earlier worlds, which counts none.</summary>
    public static RecordChangeRules CreateOtherRecordChanges(ImmutableArray<RestoredPart> restoredBefore) =>
        new(CreateOtherRecordChange(restoredBefore), CreateOtherRecordChange(restored: []));

    /// <summary>The objects that can cause removals in the run's world.</summary>
    public static ICausesOfTheRun CreateCausesOfTheRun(IBaseObjectShapes shapes, Execution execution) => new CausesOfTheRun(shapes, execution);

    /// <summary>The objects that can cause removals in worlds other than the run's.</summary>
    /// <param name="worldCaches">The object caches of the worlds asked about.</param>
    public static ICausesOfWorlds CreateCausesOfWorlds(IObjectCachesOfWorlds worldCaches) => new CausesOfWorlds(worldCaches);

    /// <summary>
    /// The views for objects standing in another world than the run's: every object of <paramref name="otherModObjects"/> can cause
    /// removals except <paramref name="excluded"/>, such as the versions of the records being checked that those objects replace.
    /// </summary>
    /// <param name="otherModObjects">The objects of the other world.</param>
    /// <param name="otherModObjectCount">The number of objects in <paramref name="otherModObjects"/>.</param>
    internal static SelectedObjects SelectForOldPlace(
        IOtherModObjectsBySpace otherModObjects, int otherModObjectCount, IEnumerable<OtherId> excluded, NpcHandling npcs) =>
        Selected(otherModObjects, Replacements.Excluding(otherModObjectCount, excluded), npcs);

    /// <summary>The objects of <paramref name="otherModObjects"/> that are not <paramref name="replacements"/>, viewed for every NPC handling.</summary>
    internal static SelectedObjects Selected(IOtherModObjectsBySpace otherModObjects, Replacements replacements, NpcHandling npcs) =>
        new(replacements, ViewsOf(otherModObjects, replacements), npcs);

    private static Dictionary<NpcHandling, IObjectsThatCanCauseRemovals> ViewsOf(IOtherModObjectsBySpace otherModObjects, Replacements replacements) =>
        Enum.GetValues<NpcHandling>().ToDictionary(
            npcs => npcs, npcs => (IObjectsThatCanCauseRemovals)new CausesView(otherModObjects, replacements, npcs));
}
