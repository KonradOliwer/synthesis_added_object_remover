using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>The target objects as the also-remove rounds left them.</summary>
/// <param name="Targets">Where they stood before any decision.</param>
/// <param name="AsMeasured">Each target object shrunk or turned at its new placement, also one removed later, as the rounds measured it.</param>
/// <param name="Decided">The decisions of every removal round so far.</param>
/// <param name="Visible">The index of <paramref name="Targets"/>.</param>
/// <param name="VisibleAsMeasured">The index of <paramref name="AsMeasured"/>.</param>
/// <param name="IndexTargets">The index of the given target objects.</param>
internal sealed record DecidedTargets(
    ImmutableArray<TargetObject> Targets,
    ImmutableArray<TargetObject> AsMeasured,
    IRemovalDecisions Decided,
    IVisibleTargetObjects Visible,
    IVisibleTargetObjects VisibleAsMeasured,
    Func<ImmutableArray<TargetObject>, IVisibleTargetObjects> IndexTargets);
