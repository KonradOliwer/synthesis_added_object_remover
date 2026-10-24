using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.WriteThePatch.Contracts;

/// <summary>What writing the patch did.</summary>
/// <param name="Removed">The target objects disabled, in the order they were written.</param>
/// <param name="Moved">The kept markers moved.</param>
/// <param name="EnableParentsReplaced">Removed objects whose Enable Parent was replaced so they stay disabled.</param>
/// <param name="Fitted">The objects given a new placement (shrunk or turned) so they fit.</param>
/// <param name="OtherModsObjectsRemoved">Other mods' objects disabled for being left without support.</param>
/// <param name="OtherModsObjectsPulledDown">Other mods' objects moved down onto what is below them.</param>
public sealed record WriteSummary(
    ImmutableArray<TargetId> Removed, int Moved, int EnableParentsReplaced, int Fitted, int OtherModsObjectsRemoved, int OtherModsObjectsPulledDown);
