using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;

/// <summary>
/// How much of the cleaned mod's drawn objects still stand in the area around a point, on one set of decisions and any objects
/// counted as removed beyond them. Thread-safe.
/// </summary>
public interface IClearedAreas
{
    /// <summary>The share standing in the area around <paramref name="point"/>; in an interior, in the whole interior.</summary>
    AreaShare Judge(RecordKey space, Vector3 point);

    /// <returns>The share standing in the area around the target, and the objects that share counts with the target itself left out.</returns>
    (AreaShare Area, int ObjectsAround) JudgeAround(TargetObject target);

    /// <summary>The areas added up under the verdict they give together.</summary>
    AreaShare Sum(IReadOnlyCollection<AreaShare> areas, AreaVerdict verdict);

    /// <summary>
    /// Those of <paramref name="targets"/> that an area judged so far with the verdict <see cref="AreaVerdict.StillStanding"/> counts
    /// among the cleaned mod's drawn objects that still stand, in the given order: the only ones whose removal can change a verdict.
    /// </summary>
    List<TargetId> StandingWhereAnAreaStillStands(IReadOnlyList<TargetId> targets);

    /// <summary>
    /// The areas with <paramref name="removed"/> counted as removed too; an area that counts none of them keeps the share these areas
    /// gave it. The new areas hold no reference to these.
    /// </summary>
    /// <param name="removed">Targets these areas count as standing.</param>
    IClearedAreas AlsoRemoving(IReadOnlyCollection<TargetId> removed);
}

/// <summary>Judges the areas of the run's targets on given decisions.</summary>
public interface IClearedAreasJudging
{
    /// <summary>The areas judged on <paramref name="decisions"/>, over the shown targets where its rounds measure them.</summary>
    IClearedAreas JudgedOn(IRemovalDecisions decisions, ClearedArea settings);
}
