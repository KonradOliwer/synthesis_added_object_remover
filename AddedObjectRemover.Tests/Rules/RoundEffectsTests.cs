using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

public class RoundEffectsTests
{
    private static readonly Round ThisRound = new(2, RoundKind.AlsoRemove);
    private static readonly Round OtherRound = new(1, RoundKind.TooClose);
    private static readonly RemovalReason Cause = new TooClose(new OtherId(0), new TooCloseCause.ObjectSize());
    private static readonly KeepReason Referenced = TestKeepReasons.Quest;

    [Fact]
    public void UntouchedHasNoOutcomeAndNoKeepReason()
    {
        Assert.Equal(new RoundEffect(RoundOutcome.Untouched, false, null), RoundEffect.Untouched);
    }

    [Fact]
    public void ARemovalForItsOwnReasonIsRemoved()
    {
        Assert.Equal(new RoundEffect(RoundOutcome.Removed, false, null), new Decision.Removed(ThisRound, Cause).Effect);
    }

    [Fact]
    public void ARemovalAsAGroupMemberIsRemovedAsLinked()
    {
        var linked = new LinkedTo(new TargetId(3));

        Assert.Equal(new RoundEffect(RoundOutcome.Removed, true, null), new Decision.Removed(ThisRound, linked).Effect);
    }

    [Fact]
    public void AKeptProposalIsKeptAsReferencedWithItsKeepReason()
    {
        Assert.Equal(new RoundEffect(RoundOutcome.KeptAsReferenced, false, Referenced), new Decision.Kept(ThisRound, Cause, Referenced).Effect);
    }

    [Fact]
    public void AFittedObjectIsKeptShrunkOrTurned()
    {
        var fitted = new Decision.Fitted(ThisRound, Cause, new EulerPlacement(Vector3.One, Vector3.Zero, 0.5f));

        Assert.Equal(new RoundEffect(RoundOutcome.KeptShrunkOrTurned, false, null), fitted.Effect);
    }

    [Fact]
    public void ADecisionOfAnotherRoundIsUntouchedForThisRound()
    {
        Assert.Equal(RoundEffect.Untouched, new Decision.Removed(OtherRound, Cause).EffectIn(ThisRound));
    }

    [Fact]
    public void ADecisionOfThisRoundReadsAsWithoutARound()
    {
        var decision = new Decision.Kept(ThisRound, Cause, Referenced);

        Assert.Equal(decision.Effect, decision.EffectIn(ThisRound));
    }
}
