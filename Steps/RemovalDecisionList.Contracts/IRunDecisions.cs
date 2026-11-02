namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>
/// The newest view of the run's removal decisions, which the step that decides next adds its rounds or moves to. Adding never
/// changes this view; only the newest view may add.
/// </summary>
public interface IRunDecisions : IRemovalDecisions
{
    /// <summary>Opens a fit round, when there is a fit.</summary>
    /// <returns>The view that includes the new round; this view when there is no fit.</returns>
    IRunDecisions ApplyFits(IReadOnlyList<ProposedFit> fits);

    /// <summary>Opens one round of <paramref name="kind"/> from the proposals; it skips an object kept shrunk or turned.</summary>
    /// <returns>The view that includes the new round.</returns>
    IRunDecisions Apply(RoundKind kind, IReadOnlyList<ProposedRemoval> proposals);

    /// <summary>Opens one round per entry, in order, each as the entry opens it.</summary>
    /// <returns>The view that includes the new rounds.</returns>
    IRunDecisions ApplyRounds(IReadOnlyList<ProposedRound> rounds);

    /// <summary>Opens one round per entry, in order, each as <paramref name="opening"/> says.</summary>
    /// <returns>The view that includes the new rounds.</returns>
    IRunDecisions ApplyRounds(RoundOpening opening, IReadOnlyList<IReadOnlyList<ProposedRemoval>> rounds);

    /// <returns>The view that includes the new moves.</returns>
    IRunDecisions AddMoves(IReadOnlyList<MarkerMove> moves);
}

/// <summary>What a step found, with the run's decisions once the step's rounds or moves are added.</summary>
public sealed record Decided<TResult>(TResult Result, IRunDecisions Decisions);
