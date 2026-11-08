using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Helpers the tests of the left-behind steps share: a removal for too close, a follow-up that removes nothing, and a kind that keeps chosen targets.</summary>
internal static class TestOneRoundSteps
{
    public static readonly TooClose TooCloseReason = new(new OtherId(0), new TooCloseCause.ObjectSize());

    public static ProposedRemoval TooClose(int index) => new(new TargetId(index), TooCloseReason);

    /// <summary>Keeps the listed targets as a kind the settings always keep.</summary>
    public sealed class KeepsTargets(params int[] kept) : IKindsAlwaysKept
    {
        public bool Keeps(TargetObject target) => kept.Contains(target.Id.Index);
    }

    /// <summary>A follow-up that removes nothing more.</summary>
    public sealed class NoFollowUp : IAlsoRemoveFollowUp
    {
        public FollowUpPass FollowUp(IRemovalDecisions decided, ImmutableArray<TargetId> seeds) => new([], Failure: null);

        public RestingObjectsResult Summarize(IRemovalDecisions final) => TestRestingObjects.SwitchedOff;
    }
}
