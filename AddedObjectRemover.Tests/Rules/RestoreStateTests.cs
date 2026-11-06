using System.Collections.Immutable;
using AddedObjectRemover.Steps.RestoreWhereRemoved;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;
using Xunit;

namespace AddedObjectRemover.Tests.Rules;

public sealed class RestoreStateTests
{
    private static readonly CloneJudged Planned = new(CloneNeed.AreaStillStands, RefusedBy: null);

    /// <summary>
    /// Candidate 0 goes back with a copy, candidate 1 without. A pass loses the copy of 0; a later pass whose guards find nothing
    /// leaves it lost, and 0 no longer goes back, with or without a copy, while 1 still does.
    /// </summary>
    [Fact]
    public void ALostCopyStaysLostAndItsCandidateStaysOutOfLaterPasses()
    {
        var start = State([null, null], [Planned, null]);
        var lostInFirstPass = new PlacedChecks([null, null], new Dictionary<int, CloneLoss> { [0] = new(CloneRefusal.ActorsWalkOnIt, Failure: null) });

        var afterFirst = Apply(start, lostInFirstPass);
        var afterSecond = Apply(afterFirst, new PlacedChecks([null, null], new Dictionary<int, CloneLoss>()));

        Assert.Equal(Planned with { RefusedBy = CloneRefusal.ActorsWalkOnIt }, afterSecond.Clones[0]);
        Assert.Equal([1], afterSecond.PlacedGoingBack());
        Assert.Empty(afterSecond.GoingBackWithClone());
        Assert.Equal(1, afterSecond.PlacedStoppedCount);
    }

    /// <summary>A candidate a guard refuses keeps its copy's outcome as it was, and its refusal stays when a later pass finds nothing.</summary>
    [Fact]
    public void ARefusalStaysAndKeepsTheCopysOutcome()
    {
        var refusal = new PlacedRefusal(PlacedGuard.StandsInChangedNavmesh, By: null, Failure: null);
        var start = State([null], [Planned]);

        var afterFirst = Apply(start, new PlacedChecks([refusal], new Dictionary<int, CloneLoss> { [0] = new(CloneRefusal.ActorsWalkOnIt, Failure: null) }));
        var afterSecond = Apply(afterFirst, new PlacedChecks([null], new Dictionary<int, CloneLoss>()));

        Assert.Equal((refusal, Planned), (afterSecond.Placed[0], afterSecond.Clones[0]));
        Assert.Empty(afterSecond.PlacedGoingBack());
    }

    private static RestoreState Apply(RestoreState state, PlacedChecks checks)
    {
        var (placed, clones) = checks.AppliedTo(state);
        return state with { Placed = placed, Clones = clones };
    }

    /// <remarks>The placed checks read no other plan, so the terrain and water plans are left out.</remarks>
    private static RestoreState State(PlacedRefusal?[] placed, CloneJudged?[] clones) =>
        new([.. placed], [.. clones], Navmeshes: null, Terrain: null!, Water: null!, ImmutableDictionary<TerrainChecked, PartFailure>.Empty);
}
