using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>
/// The "NPCs and creatures" setting in the too-close step: other mods' placed NPCs are tested
/// like any object (AlwaysLikeAnyObject), only by <see cref="NpcStuckSearch"/> (OnlyWhenStuckInsideTheObject),
/// or never (Never).
/// </summary>
internal sealed class NpcTooCloseRule
{
    private NpcTooCloseRule(NpcHandling handling, NpcStuckSearch? stuckSearch)
    {
        Handling = handling;
        StuckSearch = stuckSearch;
    }

    public NpcHandling Handling { get; }

    /// <summary>Set only for OnlyWhenStuckInsideTheObject.</summary>
    public NpcStuckSearch? StuckSearch { get; }

    /// <param name="createStuckSearch">Called only for OnlyWhenStuckInsideTheObject.</param>
    public static NpcTooCloseRule Create(NpcHandling handling, Func<NpcStuckSearch> createStuckSearch) =>
        new(handling, handling == NpcHandling.OnlyWhenStuckInsideTheObject ? createStuckSearch() : null);

    /// <summary>The first NPC stuck in the target, or null; always null unless OnlyWhenStuckInsideTheObject.</summary>
    public OtherId? FindFirstStuckNpc(int targetIndex, NpcScratch scratch) =>
        StuckSearch?.FindFirstStuckNpc(targetIndex, scratch);

    /// <summary>The first object match wins; NPCs are only looked at when no object matched.</summary>
    /// <remarks>An NPC stuck in the target is within the target's own size, whichever zones are on.</remarks>
    public ZoneHit? ThenFirstStuckNpc(ZoneHit? objectMatch, int targetIndex, NpcScratch scratch) =>
        objectMatch ?? (FindFirstStuckNpc(targetIndex, scratch) is { } npc ? new ZoneHit(npc, new TooCloseCause.ObjectSize()) : null);
}
