namespace AddedObjectRemover;

/// <summary>The report records of the ledger's decisions: removals and kept objects, round by round.</summary>
internal sealed class LedgerReport(Ledger ledger, World world, LeftoverResult leftovers)
{
    private readonly Dictionary<int, LeftoverEvaluation> _leftoverByTarget =
        leftovers.Evaluations.ToDictionary(evaluation => evaluation.TargetIndex);

    /// <summary>Every removal: round by round, each round's direct removals in target order, then the linked ones.</summary>
    public List<Removal> Removals() => [.. ledger.Rounds.SelectMany(RemovalsIn)];

    /// <summary>Every held object: round by round, in target order.</summary>
    public List<KeptTarget> Kept() => [.. ledger.Rounds.SelectMany(round => KeptIn(ledger, round))];

    public IEnumerable<Removal> RemovalsIn(Round round) =>
        ledger.RemovedIn(round).Select(target => ToRemoval(target, ledger.Of(target)!.Cause));

    public static IEnumerable<KeptTarget> KeptIn(Ledger ledger, Round round) =>
        ledger.HeldIn(round).Select(target => ToKept(target, (Verdict.Held)ledger.Of(target)!));

    public static int CountLinkedIn(Ledger ledger, Round round) =>
        ledger.RemovedIn(round).Count(target => ledger.Of(target)!.Cause is Cause.Linked);

    private Removal ToRemoval(TargetId target, Cause cause) => cause switch
    {
        Cause.TooClose tooClose => new TooCloseRemoval(target.Index, world.Rivals[tooClose.Rival.Index]),
        Cause.Touching touching => new TouchingRemoval(target.Index, touching.Touched.Index),
        Cause.LostSupport lost => new AnchoringRemoval(target.Index, lost.RemovedShare, lost.MainSupporter.Index),
        Cause.InsideRival or Cause.SurroundingsCleared => new LeftoverRemoval(target.Index, _leftoverByTarget[target.Index]),
        Cause.Linked linked => new LinkedRemoval(target.Index, linked.To.Index),
        _ => throw new InvalidOperationException($"Unknown cause {cause}."),
    };

    private static KeptTarget ToKept(TargetId target, Verdict.Held held) =>
        new(target.Index, held.Reason, TouchedRemovedTarget(held.Cause));

    private static int? TouchedRemovedTarget(Cause cause) => cause switch
    {
        Cause.Touching touching => touching.Touched.Index,
        Cause.LostSupport lost => lost.MainSupporter.Index,
        _ => null,
    };
}
