namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>What became of a box around a point: it stands, it was removed, or it was restored (it left the place the point is judged in).</summary>
public enum BoxFate
{
    Stands,
    Removed,
    Restored,
}
