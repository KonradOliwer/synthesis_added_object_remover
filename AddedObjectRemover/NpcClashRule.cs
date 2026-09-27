namespace AddedObjectRemover;

/// <summary>
/// The "NPCs and creatures" setting in the too-close step: other mods' placed NPCs are tested
/// like any object (CountLikeObjects), only by <see cref="NpcStuckSearch"/> (OnlyWhenStuckInObject),
/// or never (Ignore).
/// </summary>
internal sealed class NpcClashRule
{
    private NpcClashRule(NpcHandling handling, NpcStuckSearch? stuckSearch)
    {
        Handling = handling;
        StuckSearch = stuckSearch;
    }

    public NpcHandling Handling { get; }

    /// <summary>Set only for OnlyWhenStuckInObject.</summary>
    public NpcStuckSearch? StuckSearch { get; }

    /// <param name="createStuckSearch">Called only for OnlyWhenStuckInObject.</param>
    public static NpcClashRule Create(NpcHandling handling, Func<NpcStuckSearch> createStuckSearch) =>
        new(handling, handling == NpcHandling.OnlyWhenStuckInObject ? createStuckSearch() : null);

    /// <summary>Whether the other object takes part in the regular too-close test.</summary>
    public bool TestsLikeObject(OtherObject other) => Handling == NpcHandling.CountLikeObjects || !other.IsPlacedNpc;

    /// <summary>Index of the first NPC stuck in the target, or -1; always -1 unless OnlyWhenStuckInObject.</summary>
    public int FindFirstStuckNpc(int targetIndex, NpcScratch scratch) =>
        StuckSearch?.FindFirstStuckNpc(targetIndex, scratch) ?? -1;

    /// <summary>Adds a worker thread's NPC counters once it is done.</summary>
    public void AddStats(NpcScratch scratch) => StuckSearch?.AddStats(scratch);

    /// <summary>The first object match wins; NPCs are only looked at when no object matched.</summary>
    public int ThenFirstStuckNpc(int objectMatch, int targetIndex, NpcScratch scratch) =>
        objectMatch >= 0 ? objectMatch : FindFirstStuckNpc(targetIndex, scratch);
}
