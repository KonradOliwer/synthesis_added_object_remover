using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>Where an object stands or stood: its world box in its space.</summary>
internal readonly record struct BoxInSpace(RecordKey Space, OrientedBox Box);

/// <summary>The judged objects of any plugin, where they stand, whose box comes within the touch gap of given boxes. Thread-safe.</summary>
internal sealed class JudgedObjectsNear(IVisibleObjectsOfAnyPlugin objectsOfAnyPlugin, OtherModsObjectRules rules, float touchGap, Execution execution)
{
    /// <param name="isLeftOut">Whether an object is not wanted, whatever it touches.</param>
    /// <returns>Each object once, in id order.</returns>
    public List<OtherObject> FindTouchingAny(IReadOnlyList<BoxInSpace> boxes, Func<OtherId, bool> isLeftOut)
    {
        var touching = ParallelMap.Run(
            execution,
            boxes.Count,
            () => new ObjectQueryScratch(),
            (k, scratch) =>
            {
                objectsOfAnyPlugin.Touching(boxes[k].Space, boxes[k].Box, touchGap, scratch, scratch.Others);
                return scratch.Others.ToList();
            },
            ParallelMap.AutomaticRangeSize);
        return
        [
            .. touching.SelectMany(ids => ids)
                .Where(id => !isLeftOut(id))
                .Distinct()
                .OrderBy(id => id.Index)
                .Select(objectsOfAnyPlugin.Get)
                .Where(rules.IsJudged),
        ];
    }
}
