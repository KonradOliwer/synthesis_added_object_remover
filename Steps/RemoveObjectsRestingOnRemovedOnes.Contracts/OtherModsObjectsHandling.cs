namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <summary>What happens to another mod's object that rests on an object of the cleaned mod the run removes, shrinks or turns.</summary>
public enum OtherModsObjectsHandling
{
    KeepSupportingObject,
    RemoveFloatingObject,
    PullObjectToTheGround,
}
