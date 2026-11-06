using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.WriteThePatch.Contracts;

/// <summary>What writing the patch did.</summary>
/// <param name="Removed">The target objects a removal step disabled, in the order they were written.</param>
/// <param name="Restored">The target objects a removal step put back to their earlier version, in the order they were written.</param>
/// <param name="EnableParentsReplaced">Removed objects whose Enable Parent was replaced so they stay disabled.</param>
/// <param name="Overrides">
/// How many records written over existing ones each part has: each record is counted once, under the part whose order for it is
/// written last, the part that names what the patch holds for it; a part without any is left out. The copies added are not among them.
/// </param>
/// <param name="Clones">The copies added where an object that went back stood, in the record key order of the objects.</param>
/// <param name="RestoredParts">
/// The parts given back to the version just below the cleaned mod, of the targets a removal step or the restore's follow-up put back
/// and where the cleaned mod was removed, by record key, then by part.
/// </param>
public sealed record WriteSummary(
    ImmutableArray<TargetId> Removed,
    ImmutableArray<TargetId> Restored,
    int EnableParentsReplaced,
    ImmutableDictionary<WrittenPart, int> Overrides,
    ImmutableArray<CloneWritten> Clones,
    ImmutableArray<RestoredPart> RestoredParts);

/// <summary>What the order written last for a record does, and which step ordered it.</summary>
public enum WrittenPart
{
    /// <summary>A removal step disabled a target object.</summary>
    Removed,

    /// <summary>A removal step put a target object back to its earlier version.</summary>
    Restored,

    /// <summary>A kept marker moved out of other mods' objects.</summary>
    KeptMarkerMoved,

    /// <summary>A target object shrunk or turned to fit.</summary>
    Fitted,

    /// <summary>
    /// A record written to restore what the cleaned mod changed where it was removed: a placed record, cell, LAND record, navmesh or the
    /// navmesh info map.
    /// </summary>
    WhereRemoved,

    /// <summary>An other mod's object disabled for being left without support.</summary>
    OtherModsObjectRemoved,

    /// <summary>An other mod's object moved down onto what is below it.</summary>
    OtherModsObjectPulledDown,

    /// <summary>The restore's follow-up disabled a target object.</summary>
    FollowUpRemoved,

    /// <summary>The restore's follow-up put a target object back to its earlier version.</summary>
    FollowUpRestored,

    /// <summary>The restore's follow-up disabled an other mod's object.</summary>
    FollowUpOtherModsObjectRemoved,

    /// <summary>The restore's follow-up moved an other mod's object down.</summary>
    FollowUpOtherModsObjectPulledDown,
}

/// <summary>A copy the patch added of another mod's object that went back, where the object stood.</summary>
/// <param name="Original">The object that went back.</param>
/// <param name="Copy">The new record.</param>
public sealed record CloneWritten(RecordKey Original, RecordKey Copy);
