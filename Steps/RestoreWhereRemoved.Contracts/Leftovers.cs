using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.OtherModsObjectsLeftWithoutSupport.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>The step's own removal reason: the object of the cleaned mod still stood where an area was restored.</summary>
public sealed record LeftoverInRestoredArea : RemovalReason;

/// <summary>A kind of object a left-behind step's settings always keep.</summary>
public abstract record AlwaysKeptKind
{
    /// <summary>Listed by the step for visible objects left behind.</summary>
    public sealed record Visible(VisibleObjectKind Kind) : AlwaysKeptKind;

    /// <summary>Listed by the step for invisible objects left behind.</summary>
    public sealed record Invisible(InvisibleObjectKind Kind) : AlwaysKeptKind;
}

/// <summary>The rule of a left-behind step that keeps an object of a kind its settings list, whatever its surroundings.</summary>
public interface IKindsAlwaysKept
{
    /// <returns>The kind that keeps the object; null when the step judges no object of its kind, keeps none of it, or is switched off.</returns>
    AlwaysKeptKind? KindKeeping(TargetObject target);
}

/// <summary>The step for other mods' objects left without support, asked again once the leftovers in restored areas are removed.</summary>
public interface IOtherModsObjectsAfterLeftovers
{
    /// <param name="earlier">What that step found when it ran.</param>
    /// <param name="decisions">The decisions with the leftovers' round.</param>
    SettledAfterLeftovers Settle(OtherModsObjectsResult earlier, IRemovalDecisions decisions);
}

/// <param name="Result">What became of other mods' objects under the decisions with the leftovers' round, with the rounds of both settlings.</param>
/// <param name="Rounds">The rounds it proposes after the leftovers' round, in order, for the run's decisions to apply.</param>
public sealed record SettledAfterLeftovers(OtherModsObjectsResult Result, ImmutableArray<ProposedRound> Rounds);

/// <summary>The column of a restored area a leftover stands in.</summary>
/// <param name="Centre">Its centre, seen from above; null for an interior, which is one area.</param>
public sealed record RestoredColumn(RecordKey Space, Vector2? Centre);

/// <summary>An object of the cleaned mod that still stood in the column of a change whose area is cleared.</summary>
/// <param name="Column">The first such column it stands in, in the order the changes are judged.</param>
/// <param name="KeptAsObjectToKeep">Why it is among the objects to keep; null when it is not.</param>
/// <param name="KeptKind">The kind a left-behind step's settings always keep; null when its kind is not kept.</param>
public sealed record LeftoverJudged(TargetId Target, RestoredColumn Column, KeepReason? KeptAsObjectToKeep, AlwaysKeptKind? KeptKind)
{
    public bool IsRemoved => KeptAsObjectToKeep is null && KeptKind is null;
}
