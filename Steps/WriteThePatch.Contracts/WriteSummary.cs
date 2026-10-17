using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.WriteThePatch.Contracts;

/// <summary>What writing the patch did.</summary>
/// <param name="Removed">The target objects disabled, in the order they were written.</param>
/// <param name="EnableParentsReplaced">Removed objects whose Enable Parent was replaced so they stay disabled.</param>
/// <param name="Writes">The same writes by record, for the next run of a batch.</param>
public sealed record WriteSummary(ImmutableArray<TargetId> Removed, int Moved, int EnableParentsReplaced, RecordWrites Writes);
