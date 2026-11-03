using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>
/// Finds the cleaned mod's objects that still stand in the columns of the areas judged cleared, of every kind: those the game draws
/// are in a column as the area counts them, by their box centre where the rounds measure them; the others by their position where the rounds measure them.
/// </summary>
/// <param name="targets">Every target, by id; only the cleaned mod's own objects are leftovers.</param>
/// <param name="shownTargets">The shown targets where the rounds measure them, as the areas counted them.</param>
/// <param name="final">The decisions the areas were judged by.</param>
internal sealed class LeftoverFinder(
    ImmutableArray<TargetObject> targets, IVisibleTargetObjects shownTargets, IBaseObjectShapes shapes, ClearedAreaJudge areas, IRemovalDecisions final)
{
    /// <param name="clearedPoints">The points of the changes whose areas are cleared, in the order the changes are judged.</param>
    /// <returns>Each leftover once, with the first column it stands in, in target order.</returns>
    public List<(TargetId Target, RestoredColumn Column)> StandingIn(IEnumerable<(RecordKey Space, Vector3 Point)> clearedPoints)
    {
        var undrawnBySpace = final.TargetsAsTheRoundsMeasureThem(targets)
            .Where(target => target.Origin == TargetOrigin.CleanedMod && !shapes.VisibilityOf(target).IsShown)
            .GroupBy(target => target.SpaceKey)
            .ToDictionary(group => group.Key, group => group.ToList());
        var columns = clearedPoints.Select(point => areas.ColumnAt(point.Space, point.Point)).Distinct();
        var found = new SortedDictionary<TargetId, RestoredColumn>();
        var inColumn = new List<VisibleTargetNear>();
        foreach (var column in columns)
        {
            foreach (var target in StandingIn(column, undrawnBySpace.GetValueOrDefault(column.Space, []), inColumn)) found.TryAdd(target, column);
        }
        return [.. found.Select(entry => (entry.Key, entry.Value))];
    }

    /// <param name="undrawn">The cleaned mod's objects in the column's space the game does not draw.</param>
    /// <param name="inColumn">The caller's buffer.</param>
    private IEnumerable<TargetId> StandingIn(RestoredColumn column, List<TargetObject> undrawn, List<VisibleTargetNear> inColumn)
    {
        var (centre, radius) = (ClearedAreaJudge.CentreOf(column), areas.RadiusOf(column));
        shownTargets.InColumn(column.Space, centre, radius, inColumn);
        var drawn = inColumn.Select(near => near.Target).Where(target => targets[target.Index].Origin == TargetOrigin.CleanedMod);
        var others = undrawn.Where(target => Vector2.Distance(centre, new Vector2(target.Transform.Position.X, target.Transform.Position.Y)) <= radius).Select(target => target.Id);
        return drawn.Concat(others).Where(target => final.FateOf(target) == BoxFate.Stands).ToList();
    }
}
