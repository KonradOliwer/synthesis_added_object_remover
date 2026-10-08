using System.Collections.Immutable;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.WriteThePatch.Contracts;

/// <summary>Everything the patch will contain.</summary>
/// <param name="Remove">The target objects to disable, in target order.</param>
/// <param name="Move">The kept markers to move.</param>
public sealed record PatchPlan(ImmutableArray<TargetId> Remove, ImmutableArray<MarkerMove> Move);

/// <summary>What writing the patch did.</summary>
/// <param name="EnableParentsReplaced">Removed objects whose Enable Parent was replaced so they stay disabled.</param>
public sealed record WriteSummary(int Removed, int Moved, int EnableParentsReplaced);
