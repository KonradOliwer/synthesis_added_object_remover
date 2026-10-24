namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

public static class OtherModsObjectsLimits
{
    /// <summary>Farthest, in game units, another mod's object is pulled down; with nothing below it within this distance it is removed instead.</summary>
    public const float MaxPullDistance = 2048f;
}
