using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>The reason a target object goes back on the restore's working copy of the decisions: its areas are cleared.</summary>
public sealed record GoingBackWhereRemoved : RemovalReason;
