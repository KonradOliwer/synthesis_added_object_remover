namespace AddedObjectRemover;

/// <summary>A target object to remove (index into the scanned targets).</summary>
internal abstract record Removal(int TargetIndex);

internal sealed record TooCloseRemoval(int TargetIndex, OtherObject TooCloseTo) : Removal(TargetIndex);

internal sealed record TouchingRemoval(int TargetIndex, int TouchedTargetIndex) : Removal(TargetIndex);

/// <param name="RemovedShare">Fraction of the object's support held by removed objects.</param>
/// <param name="MainRemovedSupporter">The removed target holding the largest share of its support.</param>
internal sealed record AnchoringRemoval(int TargetIndex, float RemovedShare, int MainRemovedSupporter) : Removal(TargetIndex);

/// <summary>An invisible target object inside another mod's object or whose surrounding visible target objects were removed.</summary>
internal sealed record LeftoverRemoval(int TargetIndex, LeftoverEvaluation Evaluation) : Removal(TargetIndex);

/// <summary>A member of the linked group of a removed target object.</summary>
/// <param name="LinkedToTargetIndex">The removed member whose removal took the group along.</param>
internal sealed record LinkedRemoval(int TargetIndex, int LinkedToTargetIndex) : Removal(TargetIndex);

/// <summary>A target object that would be removed but stays because it, or a member of its linked group, is referenced.</summary>
/// <param name="TouchedTargetIndex">The removed target it touches or rests on most, if any.</param>
internal sealed record KeptTarget(int TargetIndex, KeepReason Reason, int? TouchedTargetIndex);

/// <summary>The report records of the ledger's decisions: removals and kept objects, round by round.</summary>
internal static class Decisions
{
    /// <summary>Every removal: round by round, each round's direct removals in target order, then the linked ones.</summary>
    public static List<Removal> Removals(Ledger ledger, World world, LeftoverResult leftovers)
    {
        var leftoverByTarget = LeftoversByTarget(leftovers);
        return [.. ledger.Rounds.SelectMany(round => RemovalsIn(ledger, world, leftoverByTarget, round))];
    }

    public static IEnumerable<Removal> RemovalsIn(Ledger ledger, World world, LeftoverResult leftovers, Round round) =>
        RemovalsIn(ledger, world, LeftoversByTarget(leftovers), round);

    /// <summary>Every held object: round by round, in target order.</summary>
    public static List<KeptTarget> Kept(Ledger ledger) => [.. ledger.Rounds.SelectMany(round => KeptIn(ledger, round))];

    public static IEnumerable<KeptTarget> KeptIn(Ledger ledger, Round round) =>
        ledger.HeldIn(round).Select(target => ToKept(target, (Verdict.Held)ledger.Of(target)!));

    public static int CountLinkedIn(Ledger ledger, Round round) =>
        ledger.RemovedIn(round).Count(target => ledger.Of(target)!.Cause is Cause.Linked);

    private static Dictionary<int, LeftoverEvaluation> LeftoversByTarget(LeftoverResult leftovers) =>
        leftovers.Evaluations.ToDictionary(evaluation => evaluation.TargetIndex);

    private static IEnumerable<Removal> RemovalsIn(
        Ledger ledger, World world, Dictionary<int, LeftoverEvaluation> leftoverByTarget, Round round) =>
        ledger.RemovedIn(round).Select(target => ToRemoval(target, ledger.Of(target)!.Cause, world, leftoverByTarget));

    private static Removal ToRemoval(
        TargetId target, Cause cause, World world, Dictionary<int, LeftoverEvaluation> leftoverByTarget) => cause switch
    {
        Cause.TooClose tooClose => new TooCloseRemoval(target.Index, world.Rivals[tooClose.Rival.Index]),
        Cause.Touching touching => new TouchingRemoval(target.Index, touching.Touched.Index),
        Cause.LostSupport lost => new AnchoringRemoval(target.Index, lost.RemovedShare, lost.MainSupporter.Index),
        Cause.InsideRival or Cause.SurroundingsCleared => new LeftoverRemoval(target.Index, leftoverByTarget[target.Index]),
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
