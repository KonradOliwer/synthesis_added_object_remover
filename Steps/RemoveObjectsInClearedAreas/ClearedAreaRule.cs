using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsInClearedAreas;

/// <summary>
/// The verdict on each of the cleaned mod's objects that still stands: removed when almost nothing of the cleaned mod's drawn objects
/// still stands in the area around it. Every object is judged on the decisions of the earlier steps alone, so what this step removes
/// never makes another object's area count as cleared. The rule only judges; the removal decisions keep referenced objects and remove
/// the linked groups of the removals.
/// </summary>
/// <param name="measured">The targets where the rounds measure them, so a fitted object is judged at its new place.</param>
/// <param name="areas">The areas judged on the decisions of the earlier steps.</param>
/// <param name="kindsAlwaysKept">The kinds each left-behind step's settings always keep.</param>
/// <param name="minObjectsAround">The fewest of the cleaned mod's other objects an area must count for its object to be removed.</param>
internal sealed class ClearedAreaRule(
    ImmutableArray<TargetObject> measured,
    IBaseObjectShapes shapes,
    IClearedAreas areas,
    IReadOnlyList<IKindsAlwaysKept> kindsAlwaysKept,
    ILeftBehindRoundJoin roundJoin,
    int minObjectsAround)
{
    /// <param name="decisions">The decisions of the earlier steps.</param>
    /// <returns>
    /// Target indexes, ascending, of the cleaned mod's drawn and invisible objects that a round may still remove (undecided, or kept
    /// shrunk or turned) and that are no objects to keep.
    /// </returns>
    /// <remarks>
    /// An object to keep is left undecided rather than kept by this step's round, so the steps after it still judge it: the marker
    /// moves take only the kept markers the invisible step judged. An object in a deleted cell and every member of a linked group
    /// with an object to keep are objects to keep.
    /// </remarks>
    public int[] Candidates(IRemovalDecisions decisions) =>
        [.. Enumerable.Range(0, measured.Length).Where(index => IsCandidate(measured[index], decisions))];

    public ClearedAreaCheck Judge(int targetIndex)
    {
        var target = measured[targetIndex];
        var (area, objectsAround) = areas.JudgeAround(target);
        return new ClearedAreaCheck(targetIndex, KindOf(target)!.Value, objectsAround, area, Decide(target, area, objectsAround), KeepReason: null);
    }

    /// <remarks>
    /// The minimum comes before the shares, so it is the reason named when both keep the object. It also keeps an invisible object
    /// with no drawn object of the cleaned mod around it.
    /// </remarks>
    private LeftBehindOutcome Decide(TargetObject target, AreaShare area, int objectsAround)
    {
        if (objectsAround < minObjectsAround) return LeftBehindOutcome.KeptTooFewObjectsAround;
        var byArea = area.Verdict == AreaVerdict.Cleared ? LeftBehindOutcome.RemovedInClearedArea : LeftBehindOutcome.KeptAreaStillStands;
        return roundJoin.KeptWhenProtected(byArea, isProtectedKind: kindsAlwaysKept.Any(kinds => kinds.KindKeeping(target) is not null));
    }

    private bool IsCandidate(TargetObject target, IRemovalDecisions decisions) =>
        target.Origin == TargetOrigin.CleanedMod && KindOf(target) is not null && decisions.MayStillBeRemoved(target.Id) && !decisions.IsProtected(target.Id);

    private ClearedAreaObjectKind? KindOf(TargetObject target) => ClearedAreaKinds.Of(shapes, target);
}
