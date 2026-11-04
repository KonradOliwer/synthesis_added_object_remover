using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.WriteThePatch.Contracts;

/// <summary>What writing the patch did.</summary>
/// <param name="Removed">The target objects disabled, in the order they were written.</param>
/// <param name="Restored">The target objects put back to their earlier version, in the order they were written.</param>
/// <param name="Moved">The kept markers moved.</param>
/// <param name="EnableParentsReplaced">Removed objects whose Enable Parent was replaced so they stay disabled.</param>
/// <param name="Fitted">The objects given a new placement (shrunk or turned) so they fit.</param>
/// <param name="OtherModsObjectsRemoved">Other mods' objects disabled for being left without support.</param>
/// <param name="OtherModsObjectsPulledDown">Other mods' objects moved down onto what is below them.</param>
/// <param name="WrittenWhereRemoved">
/// The records written to restore what the cleaned mod changed where it was removed: placed records, cells, LAND records, navmeshes
/// and the navmesh info map, each once. The copies added are not among them.
/// </param>
/// <param name="Clones">The copies added where an object that went back stood, in the record key order of the objects.</param>
public sealed record WriteSummary(
    ImmutableArray<TargetId> Removed,
    ImmutableArray<TargetId> Restored,
    int Moved,
    int EnableParentsReplaced,
    int Fitted,
    int OtherModsObjectsRemoved,
    int OtherModsObjectsPulledDown,
    int WrittenWhereRemoved,
    ImmutableArray<CloneWritten> Clones);

/// <summary>A copy the patch added of another mod's object that went back, where the object stood.</summary>
/// <param name="Original">The object that went back.</param>
/// <param name="Copy">The new record.</param>
public sealed record CloneWritten(RecordKey Original, RecordKey Copy);
