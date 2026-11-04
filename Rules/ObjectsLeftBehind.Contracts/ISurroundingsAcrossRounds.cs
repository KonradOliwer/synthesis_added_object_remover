using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;

/// <summary>A candidate of a left-behind step with the areas of its surroundings.</summary>
public readonly record struct MeasuredSurroundings(int TargetIndex, SectorAreas Surroundings);

/// <summary>The surroundings of a step's candidates across its rounds, measured again only where a round took a neighbour from its place.</summary>
public interface ISurroundingsAcrossRounds
{
    /// <summary>Every candidate with every other visible object standing, in target order.</summary>
    IReadOnlyList<MeasuredSurroundings> Initial { get; }

    /// <summary>Takes in the objects that the rounds of <paramref name="decisions"/> after those of the update before took from their place, with their fates there.</summary>
    /// <param name="decisions">A later view of the same decisions as at the update before.</param>
    /// <returns>The still-undecided candidates with one of those objects as a neighbour, measured again, in target order.</returns>
    IReadOnlyList<MeasuredSurroundings> Update(IRemovalDecisions decisions);
}
