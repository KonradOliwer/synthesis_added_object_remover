namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>The one reader of what became of a target object, as the steps that judge the surroundings of an object tell it.</summary>
public static class NeighbourFates
{
    public static BoxFate Of(IRemovalDecisions decisions, TargetId target)
    {
        if (decisions.IsRemoved(target)) return BoxFate.Removed;
        return decisions.HasLeftItsPlace(target) ? BoxFate.Restored : BoxFate.Stands;
    }
}
