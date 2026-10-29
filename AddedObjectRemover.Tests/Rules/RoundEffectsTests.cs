using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

public class RoundEffectsTests
{
    private static readonly Round ThisRound = new(2, RoundKind.AlsoRemove);
    private static readonly Round OtherRound = new(1, RoundKind.TooClose);
    private static readonly RemovalReason Cause = new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize());
    private static readonly KeepReason Referenced = TestKeepReasons.Quest;

    [Fact]
    public void NoDecisionIsUntouched()
    {
        Assert.Equal(new RoundEffect(RoundOutcome.Untouched, false, null), RoundEffects.Of(null));
    }

    [Fact]
    public void ARemovalForItsOwnReasonIsRemoved()
    {
        Assert.Equal(new RoundEffect(RoundOutcome.Removed, false, null), RoundEffects.Of(new Decision.Removed(ThisRound, Cause)));
    }

    [Fact]
    public void ARemovalAsAGroupMemberIsRemovedAsLinked()
    {
        var linked = new RemovalReason.LinkedTo(new TargetId(3));

        Assert.Equal(new RoundEffect(RoundOutcome.Removed, true, null), RoundEffects.Of(new Decision.Removed(ThisRound, linked)));
    }

    [Fact]
    public void AKeptProposalIsKeptAsReferencedWithItsKeepReason()
    {
        Assert.Equal(new RoundEffect(RoundOutcome.KeptAsReferenced, false, Referenced), RoundEffects.Of(new Decision.Kept(ThisRound, Cause, Referenced)));
    }

    [Fact]
    public void AFittedObjectIsKeptShrunkOrTurned()
    {
        var fitted = new Decision.Fitted(ThisRound, Cause, new EulerPlacement(Vector3.One, Vector3.Zero, 0.5f));

        Assert.Equal(new RoundEffect(RoundOutcome.KeptShrunkOrTurned, false, null), RoundEffects.Of(fitted));
    }

    [Fact]
    public void ADecisionOfAnotherRoundIsUntouchedForThisRound()
    {
        Assert.Equal(RoundOutcome.Untouched, RoundEffects.Of(new Decision.Removed(OtherRound, Cause), ThisRound).Outcome);
        Assert.Equal(RoundOutcome.Untouched, RoundEffects.Of(null, ThisRound).Outcome);
    }

    [Fact]
    public void ADecisionOfThisRoundReadsAsWithoutARound()
    {
        var decision = new Decision.Kept(ThisRound, Cause, Referenced);

        Assert.Equal(RoundEffects.Of(decision), RoundEffects.Of(decision, ThisRound));
    }
}
