namespace AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

/// <summary>How other mods' placed NPCs and creatures take part in the too-close step.</summary>
public enum NpcHandling
{
    CountLikeObjects,
    OnlyWhenStuckInObject,
    Ignore,
}
