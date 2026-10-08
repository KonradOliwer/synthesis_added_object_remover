namespace AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

/// <param name="Evaluations">In target order.</param>
public sealed record LeftBehindResult(IReadOnlyList<LeftBehindCheck> Evaluations)
{
    public static LeftBehindResult None { get; } = new([]);

    public int RemovedCount => Evaluations.Count(evaluation => evaluation.IsRemoved);

    public int CountOutcomes(LeftBehindOutcome outcome) => Evaluations.Count(evaluation => evaluation.Decision == outcome);
}
