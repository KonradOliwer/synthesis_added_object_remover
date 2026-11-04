using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

/// <param name="Npcs">How other mods' placed NPCs and creatures take part.</param>
public sealed record SelectionSettings(NpcHandling Npcs);
