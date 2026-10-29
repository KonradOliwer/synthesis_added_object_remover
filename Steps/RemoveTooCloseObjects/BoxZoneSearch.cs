using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>The BoundingBox removal zone (<see cref="TooCloseSearch"/>) as an <see cref="ITooCloseSearch"/>; it tests no large other-mod object apart.</summary>
internal sealed class BoxZoneSearch(
    IReadOnlyList<TargetObject> targets,
    IObjectsThatCanCauseRemovals otherModObjects,
    IBaseObjectShapes shapes,
    ImmutableArray<ZoneGrowth> zones,
    NpcTooCloseRule npcRule) : ITooCloseSearch
{
    public int LargeOtherObjects => 0;

    public TooCloseSearchResult FindTooCloseTargets(ImmutableArray<TargetId> judged, WorkOrder order, Execution execution) =>
        TooCloseSearch.FindTooCloseTargets(targets, otherModObjects, shapes, zones, npcRule, judged, order, execution);

    public ITargetTries StartTries() => new EachCheckedAlone(CheckAlone);

    private TargetCheck CheckAlone(TargetObject placed) => TooCloseSearch.CheckAlone(placed, otherModObjects, shapes, zones, npcRule);

    private sealed class EachCheckedAlone(Func<TargetObject, TargetCheck> checkAlone) : ITargetTries
    {
        public TargetCheck Check(TargetObject placed) => checkAlone(placed);

        public void Dispose()
        {
        }
    }
}
