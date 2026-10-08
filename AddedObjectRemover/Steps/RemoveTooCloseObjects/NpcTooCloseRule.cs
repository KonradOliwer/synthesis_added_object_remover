using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>
/// The "NPCs and creatures" setting in the too-close step: other mods' placed NPCs are tested
/// like any object (CountLikeObjects), only by <see cref="NpcStuckSearch"/> (OnlyWhenStuckInObject),
/// or never (Ignore).
/// </summary>
internal sealed class NpcTooCloseRule
{
    private NpcTooCloseRule(NpcHandling handling, NpcStuckSearch? stuckSearch)
    {
        Handling = handling;
        StuckSearch = stuckSearch;
    }

    public NpcHandling Handling { get; }

    /// <summary>Set only for OnlyWhenStuckInObject.</summary>
    public NpcStuckSearch? StuckSearch { get; }

    /// <param name="createStuckSearch">Called only for OnlyWhenStuckInObject.</param>
    public static NpcTooCloseRule Create(NpcHandling handling, Func<NpcStuckSearch> createStuckSearch) =>
        new(handling, handling == NpcHandling.OnlyWhenStuckInObject ? createStuckSearch() : null);

    /// <summary>The first NPC stuck in the target, or null; always null unless OnlyWhenStuckInObject.</summary>
    public OtherId? FindFirstStuckNpc(int targetIndex, NpcScratch scratch) =>
        StuckSearch?.FindFirstStuckNpc(targetIndex, scratch);

    /// <summary>The first object match wins; NPCs are only looked at when no object matched.</summary>
    public OtherId? ThenFirstStuckNpc(OtherId? objectMatch, int targetIndex, NpcScratch scratch) =>
        objectMatch ?? FindFirstStuckNpc(targetIndex, scratch);
}
