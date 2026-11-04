using System.Numerics;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Run.RunAllSteps;

/// <summary>Builds the areas' index when the first area is judged, so a step that judges none never pays for it. Thread-safe.</summary>
internal sealed class LazyClearedAreas(Func<IClearedAreas> create) : IClearedAreas
{
    private readonly Lazy<IClearedAreas> _areas = new(create, LazyThreadSafetyMode.ExecutionAndPublication);

    public AreaShare Judge(RecordKey space, Vector3 point) => _areas.Value.Judge(space, point);

    public (AreaShare Area, int ObjectsAround) JudgeAround(TargetObject target) => _areas.Value.JudgeAround(target);

    public AreaShare Sum(IReadOnlyCollection<AreaShare> areas, AreaVerdict verdict) => _areas.Value.Sum(areas, verdict);
}
