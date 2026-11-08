using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

using static FollowUpScenes;

/// <summary>The follow-up of what goes back gives the same answers whatever the number of workers, and each pass answers as a new follow-up would.</summary>
public sealed class FollowUpOfGoingBackWorkersTests
{
    private const int StackHeight = 3;

    /// <summary>Stacks of three rocks like the one above side by side, some of their rocks of an unrelated mod: the same with one worker as with eight.</summary>
    [Fact]
    public void ArrivalsThatDoNotFitAreTheSameWhateverTheNumberOfWorkers()
    {
        var stacks = ManyStacks();
        HashSet<int> goingBack = [.. Enumerable.Range(0, stacks.Rocks.Count).Where(rock => rock % StackHeight != 0)];

        var one = stacks.CheckOneAfterAnother(threads: 1, oneFollowUpForAll: true, [goingBack])[0];
        var eight = stacks.CheckOneAfterAnother(threads: 8, oneFollowUpForAll: true, [goingBack])[0];

        Assert.Equal(one.Written.Others.Select(other => other.Outcome), eight.Written.Others.Select(other => other.Outcome));
        Assert.Equal(WrittenOthers(one), WrittenOthers(eight));
        Assert.Equal(one.ArrivingWhereTheyDoNotFit.Keys, eight.ArrivingWhereTheyDoNotFit.Keys);
        Assert.Equal(
            one.ArrivingWhereTheyDoNotFit.Values.Select(refused => (refused.Misfit, string.Join(", ", refused.Causes))),
            eight.ArrivingWhereTheyDoNotFit.Values.Select(refused => (refused.Misfit, string.Join(", ", refused.Causes))));
        Assert.NotEmpty(one.Written.Others);
        Assert.NotEmpty(one.ArrivingWhereTheyDoNotFit);
    }

    [Fact]
    public void TheFollowUpGivesTheSameResultsWhateverTheNumberOfWorkers()
    {
        var one = ManyRocks().Check(threads: 1);
        var eight = ManyRocks().Check(threads: 8);

        Assert.Equal(WrittenTargets(one), WrittenTargets(eight));
        Assert.Equal(WrittenOthers(one), WrittenOthers(eight));
        Assert.Equal(one.Written.Others.Select(other => other.Outcome), eight.Written.Others.Select(other => other.Outcome));
        Assert.NotEmpty(one.Written.Targets);
        Assert.NotEmpty(one.Written.Others);
    }

    /// <summary>
    /// The passes ask one follow-up about ever fewer rocks going back, and it keeps the weights before it measured between them: each
    /// answer is the one a new follow-up gives, with one worker and with four.
    /// </summary>
    [Fact]
    public void AFollowUpAskedPassAfterPassAnswersAsANewOneEachTimeWhateverTheNumberOfWorkers()
    {
        List<HashSet<int>> passes = [[.. Enumerable.Range(0, 30)], [.. Enumerable.Range(0, 30).Where(rock => rock % 2 == 0)], [.. Enumerable.Range(0, 10)]];

        var fresh = ManyRocks().CheckOneAfterAnother(threads: 1, oneFollowUpForAll: false, passes);
        Assert.All(
            [ManyRocks().CheckOneAfterAnother(threads: 1, oneFollowUpForAll: true, passes), ManyRocks().CheckOneAfterAnother(threads: 4, oneFollowUpForAll: true, passes)],
            shared =>
            {
                Assert.Equal(fresh.Select(WrittenTargets), shared.Select(WrittenTargets));
                Assert.Equal(fresh.Select(WrittenOthers), shared.Select(WrittenOthers));
                Assert.Equal(fresh.SelectMany(checks => checks.Written.Others.Select(other => other.Outcome)), shared.SelectMany(checks => checks.Written.Others.Select(other => other.Outcome)));
            });
        Assert.All(fresh, checks => Assert.NotEmpty(checks.Written.Others));
    }

    /// <summary>
    /// Thirty rocks, every one leaving; every second carries a pebble of the cleaned mod, every third a base game lantern with a pebble
    /// on it, every fifth an unrelated mod's lantern with a pebble on it.
    /// </summary>
    private static ManyRockScene ManyRocks()
    {
        const int Count = 30;
        List<RecordChangedByCleanedMod> rocks = [];
        List<OtherObject> others = [];
        List<Vector3> pebbles = [];
        for (var index = 0; index < Count; index++)
        {
            var now = new Vector3(100 + index * 100, 2000, HalfSize);
            rocks.Add(Rock(index, now));
            others.Add(RockObject(index, now));
            if (index % 2 == 0) pebbles.Add(now + new Vector3(-10, -10, HalfSize));
            if (index % 3 == 0)
            {
                others.Add(Placed(BaseGameMod, Count + index, Lantern, now + new Vector3(10, 10, HalfSize)));
                pebbles.Add(now + new Vector3(10, 10, HalfSize + 6));
            }
            if (index % 5 == 0)
            {
                others.Add(Placed(LaterMod, 2 * Count + index, Lantern, now + new Vector3(-10, 10, HalfSize)));
                pebbles.Add(now + new Vector3(-10, 10, HalfSize + 6));
            }
        }
        return new ManyRockScene(rocks, others, pebbles);
    }

    /// <summary>
    /// Eight stacks of three rocks the cleaned mod moved, still stacked, from 1000 units north: bottom, middle and top, in that index
    /// order. Every fourth middle rock, from the second stack on, and every third top rock are of an unrelated mod.
    /// </summary>
    private static ManyRockScene ManyStacks()
    {
        const int Count = 8;
        List<RecordChangedByCleanedMod> rocks = [];
        List<OtherObject> others = [];
        for (var stack = 0; stack < Count; stack++)
        {
            for (var level = 0; level < StackHeight; level++)
            {
                var index = stack * StackHeight + level;
                var mod = (level == 1 && stack % 4 == 1) || (level == 2 && stack % 3 == 0) ? LaterMod : BaseGameMod;
                var now = new Vector3(100 + stack * 200, 3000, HalfSize + level * Top);
                rocks.Add(Rock(index, now, mod));
                others.Add(RockObject(index, now, mod));
            }
        }
        return new ManyRockScene(rocks, others, []);
    }

    /// <summary>Rocks of the base game the cleaned mod moved, and objects resting on them.</summary>
    private sealed record ManyRockScene(List<RecordChangedByCleanedMod> Rocks, List<OtherObject> Others, List<Vector3> Pebbles)
    {
        public FollowUpChecks Check(int threads) => CheckOneAfterAnother(threads, oneFollowUpForAll: true, [[.. Enumerable.Range(0, Rocks.Count)]])[0];

        /// <summary>The follow-up of each set of rocks going back, in order: asked of one follow-up, as the passes ask it, or of a new one each time.</summary>
        public List<FollowUpChecks> CheckOneAfterAnother(int threads, bool oneFollowUpForAll, IReadOnlyList<HashSet<int>> goingBackSets)
        {
            ImmutableArray<TargetObject> targets = [.. Pebbles.Select((place, index) => PebbleTarget(index, place))];
            var collected = TestScenes.CreateWorld(targets, [.. Others.Select((other, index) => other with { Id = new OtherId(index) })]) with { ChangedRecords = [.. Rocks] };
            FollowUpOfGoingBack Create() => new(RuleOver(collected, threads, Support()), DecisionsOver(targets), NothingTakenAway, Rocks, collected, landUnder: (_, _) => null);
            var shared = Create();
            return
            [
                .. goingBackSets.Select(goingBack =>
                    (oneFollowUpForAll ? shared : Create()).Check(goingBack, new HashSet<int>(), PassOf(collected, Rocks, goingBack), land: null)),
            ];
        }
    }
}
