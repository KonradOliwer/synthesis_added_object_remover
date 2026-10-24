namespace AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

/// <summary>How other mods' placed NPCs and creatures take part in the too-close step.</summary>
public enum NpcHandling
{
    AlwaysLikeAnyObject,
    OnlyWhenStuckInsideTheObject,

    /// <summary>As <see cref="OnlyWhenStuckInsideTheObject"/>, but only objects whose mesh collision stops characters count.</summary>
    OnlyWhenStuckInsideTheObjectWithCollision,
    Never,
}
