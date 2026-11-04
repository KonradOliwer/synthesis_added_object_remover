using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;

/// <summary>How much of the cleaned mod's drawn objects still stand in the area around a point, on one set of decisions. Thread-safe.</summary>
public interface IClearedAreas
{
    /// <summary>The share standing in the area around <paramref name="point"/>; in an interior, in the whole interior.</summary>
    AreaShare Judge(RecordKey space, Vector3 point);

    /// <returns>The share standing in the area around the target, and the objects that share counts with the target itself left out.</returns>
    (AreaShare Area, int ObjectsAround) JudgeAround(TargetObject target);

    /// <summary>The areas added up under the verdict they give together.</summary>
    AreaShare Sum(IReadOnlyCollection<AreaShare> areas, AreaVerdict verdict);
}

/// <summary>Judges the areas of the run's targets on given decisions.</summary>
public interface IClearedAreasJudging
{
    /// <summary>The areas judged on <paramref name="decisions"/>, over the shown targets where its rounds measure them.</summary>
    IClearedAreas JudgedOn(IRemovalDecisions decisions, ClearedArea settings);
}
