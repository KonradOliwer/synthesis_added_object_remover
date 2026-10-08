using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;

/// <summary>What one also-remove round found on the way, for the reports.</summary>
public abstract record RoundDetails;

/// <param name="Reached">Each object reached by touch, paired with the frontier object that touched it first.</param>
/// <param name="Work">The round's pair tests.</param>
public sealed record TouchRoundDetails(ImmutableArray<IndexPair> Reached, PairTestStats Work) : RoundDetails;

/// <param name="Evaluations">In target order.</param>
/// <param name="Work">The pair tests that found the round's candidates.</param>
public sealed record SupportRoundDetails(ImmutableArray<SupportEvaluation> Evaluations, PairTestStats Work) : RoundDetails;
