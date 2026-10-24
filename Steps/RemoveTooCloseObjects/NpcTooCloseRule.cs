using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>
/// The "NPCs and creatures" setting in the too-close step: other mods' placed NPCs are tested
/// like any object (AlwaysLikeAnyObject), only by <see cref="NpcStuckSearch"/> (OnlyWhenStuckInsideTheObject,
/// and OnlyWhenStuckInsideTheObjectWithCollision for objects whose collision stops characters), or never (Never).
/// </summary>
internal sealed class NpcTooCloseRule
{
    private NpcTooCloseRule(NpcHandling handling, NpcStuckSearch? stuckSearch)
    {
        Handling = handling;
        StuckSearch = stuckSearch;
    }

    public NpcHandling Handling { get; }

    /// <summary>Set only when NPCs count when stuck in an object.</summary>
    public NpcStuckSearch? StuckSearch { get; }

    /// <param name="createStuckSearch">
    /// Called only when NPCs count when stuck in an object, with whether only objects whose collision stops characters count.
    /// </param>
    public static NpcTooCloseRule Create(NpcHandling handling, Func<bool, NpcStuckSearch> createStuckSearch) =>
        new(handling, CountsStuckNpcs(handling) ? createStuckSearch(handling == NpcHandling.OnlyWhenStuckInsideTheObjectWithCollision) : null);

    private static bool CountsStuckNpcs(NpcHandling handling) =>
        handling is NpcHandling.OnlyWhenStuckInsideTheObject or NpcHandling.OnlyWhenStuckInsideTheObjectWithCollision;

    /// <summary>The first NPC stuck in the target, or null; always null unless NPCs count when stuck in an object.</summary>
    public OtherId? FindFirstStuckNpc(TargetObject target, NpcScratch scratch) =>
        StuckSearch?.FindFirstStuckNpc(target, scratch);

    /// <summary>The first object match wins; NPCs are only looked at when no object matched.</summary>
    /// <remarks>An NPC stuck in the target is within the target's own size, whichever zones are on.</remarks>
    public ZoneHit? ThenFirstStuckNpc(ZoneHit? objectMatch, TargetObject target, NpcScratch scratch) =>
        objectMatch ?? (FindFirstStuckNpc(target, scratch) is { } npc ? new ZoneHit(npc, new TooCloseCause.ObjectSize()) : null);
}
