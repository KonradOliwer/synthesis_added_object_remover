namespace AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

/// <summary>What the select step hands on: the objects a target replaces, and the other mods' objects that can cause removals.</summary>
/// <param name="ViewByNpcHandling">The objects that can cause removals for each way of handling other mods' placed NPCs.</param>
/// <param name="StandardNpcs">The NPC handling of the settings of the objects these were selected for.</param>
public sealed record SelectedObjects(
    Replacements Replacements, IReadOnlyDictionary<NpcHandling, IObjectsThatCanCauseRemovals> ViewByNpcHandling, NpcHandling StandardNpcs)
{
    /// <summary>The objects that can cause removals for the NPC handling of <see cref="StandardNpcs"/>.</summary>
    public IObjectsThatCanCauseRemovals ObjectsThatCanCauseRemovals => ViewFor(StandardNpcs);

    public IObjectsThatCanCauseRemovals ViewFor(NpcHandling npcs) => ViewByNpcHandling[npcs];
}
