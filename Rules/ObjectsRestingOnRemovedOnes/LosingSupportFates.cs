using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>
/// The fate of a judged object that loses its support, by the handling: an object the patch cannot change (a later plugin wins it,
/// or it stands in a cell the final world deletes) is left as it is, and so, with RemoveFloatingObject, is one that may not be
/// removed; with PullObjectToTheGround it is pulled down onto what is below it, or removed when nothing is below within the pull
/// limit, or left when it still rests where it stands.
/// </summary>
internal sealed class LosingSupportFates(OtherModsObjectRules rules, DropFinder drops, OtherModsObjectsSettings settings)
{
    /// <returns>The fate known before any object is pulled down; null for one waiting to be pulled down.</returns>
    public OtherModsObjectOutcome? FateBeforePulling(OtherObject other, float lostShare)
    {
        if (!rules.PatchCanChange(other)) return Left(other, OtherModsObjectFate.LeftWonAfterPatch, lostShare, ReferenceRoles.None);
        if (other.CellDeleted is not null) return Left(other, OtherModsObjectFate.LeftInDeletedCell, lostShare, ReferenceRoles.None);
        if (settings.Handling == OtherModsObjectsHandling.PullObjectToTheGround) return null;
        return RemovedUnlessBlocked(other, OtherModsObjectFate.Removed, OtherModsObjectFate.LeftNotRemovable, lostShare);
    }

    /// <param name="drop">How far below it the object lands; null when nothing is below it within the pull limit.</param>
    public OtherModsObjectOutcome FateOfPull(OtherObject other, float lostShare, float? drop)
    {
        if (drop is not { } distance) return RemovedUnlessBlocked(other, OtherModsObjectFate.RemovedNothingBelow, OtherModsObjectFate.LeftNothingBelowNotRemovable, lostShare);
        return drops.RestsAt(distance)
            ? Left(other, OtherModsObjectFate.LeftStillTouching, lostShare, ReferenceRoles.None)
            : new OtherModsObjectOutcome(other, OtherModsObjectFate.PulledDown, lostShare, distance, ReferenceRoles.None);
    }

    /// <param name="left">The fate when its references keep it from being removed.</param>
    private static OtherModsObjectOutcome RemovedUnlessBlocked(OtherObject other, OtherModsObjectFate removed, OtherModsObjectFate left, float lostShare) =>
        OtherModsObjectRules.RemovalBlockers(other) is var blockers and not ReferenceRoles.None
            ? Left(other, left, lostShare, blockers)
            : new OtherModsObjectOutcome(other, removed, lostShare, Drop: 0f, ReferenceRoles.None);

    private static OtherModsObjectOutcome Left(OtherObject other, OtherModsObjectFate fate, float lostShare, ReferenceRoles blockers) =>
        new(other, fate, lostShare, Drop: 0f, blockers);
}
