using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

/// <summary>One run of a step over the objects judged with one settings set.</summary>
public interface IJudgedPass
{
    /// <summary><see cref="StepOutcome.SwitchedOff"/> when the settings switch the step off; the pass then holds nothing.</summary>
    StepOutcome Outcome { get; }

    /// <summary>In target order.</summary>
    ImmutableArray<ProposedRemoval> Proposals { get; }

    /// <summary>The target objects whose check failed unexpectedly, in target order.</summary>
    ImmutableArray<PartFailure> Failures { get; }
}

/// <summary>What a step that judges groups of objects found: one pass for each distinct settings set.</summary>
/// <param name="Passes">In the order their settings first appeared.</param>
public abstract record JudgedPasses<TPass>(ImmutableArray<TPass> Passes)
    where TPass : IJudgedPass
{
    /// <summary><see cref="StepOutcome.SwitchedOff"/> when every pass is.</summary>
    public StepOutcome Outcome => Passes.Any(pass => pass.Outcome == StepOutcome.Performed) ? StepOutcome.Performed : StepOutcome.SwitchedOff;

    /// <summary>The passes' proposals in target order.</summary>
    public ImmutableArray<ProposedRemoval> Proposals => [.. Passes.SelectMany(pass => pass.Proposals).OrderBy(proposal => proposal.Target)];

    /// <summary>The passes' failures in target order; the failures of a whole part come first, in pass order.</summary>
    public ImmutableArray<PartFailure> Failures => [.. Passes.SelectMany(pass => pass.Failures).OrderBy(failure => failure.Subject?.Position)];
}
