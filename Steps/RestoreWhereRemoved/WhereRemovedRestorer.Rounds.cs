using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

public static partial class WhereRemovedRestorer
{
    /// <summary>
    /// Restores in rounds, so the areas are judged after all removals: when the follow-up of what went back removes cleaned mod's
    /// objects that stand in an area still standing, the areas are judged again with them counted as removed, and a new round plans and
    /// guards everything on the new verdicts, unless no decision changes. Earlier rounds win: what a round put back keeps going back,
    /// and what it removed stays removed (<see cref="RunLaterRound"/>).
    /// </summary>
    /// <remarks>
    /// Each judging again counts at least one more of the cleaned mod's objects as removed and none as standing again, so there are at
    /// most as many as the cleaned mod's objects the areas count.
    /// </remarks>
    private static RestoreWhereRemovedResult PlanEverything(Input input)
    {
        var clock = input.TimeSinceStart;
        var ((inputs, candidates, first), setUpTime) = Timed.Run(clock, () => SetUp(input));
        var round = RunRound(inputs, first, EarlierRound.None, RoundsBefore.None.WithJudgingTime(setUpTime));
        while (round.Judged.Areas.StandingWhereAnAreaStillStands(RemovedByFollowUp(round)) is { Count: > 0 } removed)
        {
            var (judged, judgingTime) = Timed.Run(clock, () => JudgeAgain(inputs, candidates, round.Judged, removed));
            round = round with { SoFar = round.SoFar.WithJudgingTime(judgingTime) };
            if (SameDecisions(round.Judged.Verdicts, judged.Verdicts)) return ResultOf(round, judged.Verdicts, input.Settings);
            round = RunLaterRound(inputs, judged, round);
        }
        ThrowWhenACountedRemovalStands(round);
        return ResultOf(round, round.Judged.Verdicts, input.Settings);
    }

    /// <summary>The areas of <paramref name="before"/> with <paramref name="removed"/> counted as removed too, and the candidates judged in them.</summary>
    private static JudgedAreas JudgeAgain(StepInputs inputs, Candidates candidates, JudgedAreas before, List<TargetId> removed)
    {
        var areas = before.Areas.AlsoRemoving(removed);
        return new JudgedAreas(areas, Judge(inputs, candidates, areas), before.Counted.Union(removed));
    }

    /// <summary>
    /// A round on areas judged again. While it would undo the round before (a placed record, water level, navmesh group or terrain
    /// quarter that went back there does not, or a removal its areas count does not happen), the parts of it that its refusals, water
    /// keepers, navmesh refusals and follow-up name as the cause are held as the round before left them (<see cref="Blame"/>) and it
    /// runs again; every other part goes back as its areas say. When nothing more can be named, it runs once more with every part only
    /// it lets go back held, so it stands as the round before on the new areas.
    /// </summary>
    /// <remarks>
    /// Each try holds at least one more part than the one before, so the tries end. With every new part held, the plans are those of
    /// the round before with the same copies and counted removals, so a try that still undoes it is a bug.
    /// </remarks>
    private static RestoreRound RunLaterRound(StepInputs inputs, JudgedAreas judged, RestoreRound before)
    {
        var earlier = EarlierRound.None;
        var soFar = before.SoFar;
        while (true)
        {
            var round = RunRound(inputs, judged, earlier, soFar);
            if (FirstUndone(inputs, round, before) is not { } undone) return round;
            var blamed = Blame(inputs, round, before).Except(earlier.Pins);
            if (blamed.IsEmpty) blamed = EverythingNew(inputs, round, before, undone).Except(earlier.Pins);
            if (blamed.IsEmpty) throw new UnreachableException($"Round {round.SoFar.Rounds} of the restore holds every part only it lets go back and still undoes {undone}.");
            earlier = EarlierRound.Holding(earlier.Pins.Plus(blamed), before);
            soFar = round.SoFar;
        }
    }

    /// <summary>One round: the plans made on the verdicts, with what it holds of the round before, and guarded until a pass changes nothing.</summary>
    /// <param name="before">What the rounds before it added up to.</param>
    private static RestoreRound RunRound(StepInputs inputs, JudgedAreas judged, EarlierRound earlier, RoundsBefore before)
    {
        var clock = inputs.Input.TimeSinceStart;
        var used = earlier.Pinned(judged.Verdicts);
        var (plans, plansTime) = Timed.Run(clock, () => PlanKinds(inputs, used, earlier));
        var (guards, guardsTime) = Timed.Run(clock, () => RestoreGuardsBuilder.Build(inputs, plans));
        var loop = new RestoreLoop(guards.Placed, guards.Terrain, guards.LoopInputs, clock);
        var ((state, followUp, passes, inPasses, terrainFindingsLeft), passesTime) = Timed.Run(clock, () => loop.Run(plans.Start));
        var times = new RestoreWhereRemovedTimes(plansTime, guards.WorldTime, guardsTime - guards.WorldTime, passesTime, inPasses);
        return new RestoreRound(judged, earlier, used, plans, state, followUp, terrainFindingsLeft, before.Plus(passes, times));
    }

    /// <remarks>Every round kept passed this check against the round before, so a counted removal that does not happen is a bug.</remarks>
    private static void ThrowWhenACountedRemovalStands(RestoreRound round)
    {
        if (FirstCountedStanding(round) is { } standing) throw new UnreachableException($"The restore leaves target {standing} standing, which its areas count as removed.");
    }

    /// <returns>The first target the round's areas count as removed that its follow-up does not remove; null when there is none.</returns>
    private static TargetId? FirstCountedStanding(RestoreRound round)
    {
        var removed = RemovedByFollowUp(round).ToHashSet();
        return round.Judged.Counted.OrderBy(target => target.Index).Where(target => !removed.Contains(target)).Select(target => (TargetId?)target).FirstOrDefault();
    }

    /// <summary>The keys of the placed records that go back in the round, in key order.</summary>
    private static IEnumerable<RecordKey> GoingBack(RestoreRound round) => GoingBackIndexes(round).Select(index => round.Plans.Cleared[index].Key);

    private static IEnumerable<int> GoingBackIndexes(RestoreRound round) => Enumerable.Range(0, round.Plans.Cleared.Count).Where(index => round.State.Placed[index] is null);

    /// <summary>The targets the round's follow-up of what went back removes, in target order.</summary>
    private static List<TargetId> RemovedByFollowUp(RestoreRound round) =>
        [.. round.FollowUp.Targets.Where(followed => followed.RestoredTo is null).Select(followed => followed.Target)];

    /// <summary>Whether every candidate is decided alike: what goes back, what keeps it and with what copy, by kind.</summary>
    private static bool SameDecisions(AreaVerdicts first, AreaVerdicts second) =>
        first.Placed.Select(PlacedDecision).SequenceEqual(second.Placed.Select(PlacedDecision))
        && first.Cells.Select(cell => (cell.Never, cell.IsRestored, cell.KeptForArea)).SequenceEqual(second.Cells.Select(cell => (cell.Never, cell.IsRestored, cell.KeptForArea)))
        && first.Lands.Length == second.Lands.Length && first.Lands.Zip(second.Lands).All(lands => SameDecision(lands.First, lands.Second))
        && first.Navmeshes.Select(NavmeshDecision).SequenceEqual(second.Navmeshes.Select(NavmeshDecision));

    private static (NeverRestoredReason? Never, bool IsRestored, AreaVerdict? KeptForArea, CloneJudged? Cloning) PlacedDecision(PlacedChangeJudged change) =>
        (change.Never, change.IsRestored, change.KeptForArea, change.Cloning);

    private static bool SameDecision(LandAreasJudged first, LandAreasJudged second) =>
        first.Never == second.Never && first.Quarters.Select(quarter => quarter.Verdict).SequenceEqual(second.Quarters.Select(quarter => quarter.Verdict));

    private static (NeverRestoredReason? Never, AreaVerdict? KeptForArea, bool NothingGoesBack) NavmeshDecision(NavmeshChangeJudged navmesh) =>
        (navmesh.Never, navmesh.KeptForArea, navmesh.NothingGoesBack);

    /// <summary>
    /// What every candidate became in the last round, once the guards between the kinds change nothing more, with the shares of
    /// <paramref name="current"/>, verdicts that decide every candidate as the last round's own judging does, and each part held for an
    /// earlier round showing what it is held for.
    /// </summary>
    private static RestoreWhereRemovedResult ResultOf(RestoreRound last, AreaVerdicts current, WhereRemovedRestoreSettings settings)
    {
        var (plans, state, earlier) = (last.Plans, last.State, last.Earlier);
        var objects = PlacedJudgments.WithOutcomes(earlier.Pinned(current).Placed, plans.Cleared, state.Placed, state.Clones);
        return new RestoreWhereRemovedResult(
            StepOutcome.Performed,
            objects,
            WithSharesOf(state.Water.Judged(), current.Cells, CellKeyOf, CellKeyOf, (cell, shared) => cell with { Area = shared.Area }),
            settings.Terrain
                ? earlier.Marked(WithSharesOf(state.Terrain.Judged(), current.Lands, land => land.Change.LandKey, LandKeyOf, (land, shared) => land with { Quarters = shared.Quarters }), current)
                : [],
            earlier.Marked(WithSharesOf(state.Navmeshes?.Navmeshes() ?? [], current.Navmeshes, NavmeshKeyOf, NavmeshKeyOf, (navmesh, shared) => navmesh with { Area = shared.Area })),
            state.Navmeshes?.Groups() ?? [],
            last.SoFar.Passes,
            plans.FinalPresence.DoorsEndingRemoved(state.PlacedGoingBack().Contains),
            [.. PlacedJudgments.FailedChecks(objects), .. state.TerrainCheckFailures()],
            last.SoFar.Times,
            last.TerrainFindingsLeft) { FollowUp = last.FollowUp, Rounds = last.SoFar.Rounds, KeptForEarlierRound = last.Earlier.Pins.KeptOtherThanPlaced() };
    }

    /// <summary>Each judged change with the share of the same change in <paramref name="shares"/>; a plan judges each change it was given once.</summary>
    private static ImmutableArray<TJudged> WithSharesOf<TJudged, TShared>(
        ImmutableArray<TJudged> judged, IEnumerable<TShared> shares, Func<TJudged, RecordKey> keyOf, Func<TShared, RecordKey> sharedKeyOf, Func<TJudged, TShared, TJudged> withShare)
    {
        var byKey = shares.ToDictionary(sharedKeyOf);
        return [.. judged.Select(change => withShare(change, byKey[keyOf(change)]))];
    }

    /// <summary>The areas a round judges its candidates in, with what they count beyond the removal steps' decisions.</summary>
    /// <param name="Counted">The targets the areas count as removed although the removal steps' decisions leave them standing.</param>
    private sealed record JudgedAreas(IClearedAreas Areas, AreaVerdicts Verdicts, ImmutableHashSet<TargetId> Counted);

    /// <summary>How a round ended.</summary>
    /// <param name="Judged">The areas and the verdicts they gave.</param>
    /// <param name="Earlier">What the round held of the round before it.</param>
    /// <param name="Used">The verdicts its plans were made on: those of <paramref name="Judged"/> with what it held.</param>
    /// <param name="FollowUp">The follow-up of the placed records going back in its last pass.</param>
    /// <param name="TerrainFindingsLeft">How many changed-terrain findings were left with nothing to keep.</param>
    /// <param name="SoFar">This round and the ones before it, added up.</param>
    private sealed record RestoreRound(
        JudgedAreas Judged, EarlierRound Earlier, AreaVerdicts Used, Plans Plans, RestoreState State, RestoreFollowUp FollowUp, int TerrainFindingsLeft, RoundsBefore SoFar);

    /// <summary>What the rounds so far add up to.</summary>
    /// <param name="Passes">The passes of all of them.</param>
    /// <param name="Times">Where all of them spent their time.</param>
    private sealed record RoundsBefore(int Rounds, int Passes, RestoreWhereRemovedTimes Times)
    {
        public static RoundsBefore None { get; } = new(0, 0, RestoreWhereRemovedTimes.None);

        /// <summary>These rounds and one more, which ran the passes and spent the times.</summary>
        public RoundsBefore Plus(int passes, RestoreWhereRemovedTimes times) =>
            new(
                Rounds + 1,
                Passes + passes,
                new(
                    Times.PlansMade + times.PlansMade,
                    Times.WorldAfterRemoval + times.WorldAfterRemoval,
                    Times.GuardsSetUp + times.GuardsSetUp,
                    Times.Passes + times.Passes,
                    Times.InPasses.Plus(times.InPasses)));

        /// <summary>With the time of judging the areas added to the time of making the plans.</summary>
        public RoundsBefore WithJudgingTime(TimeSpan time) => this with { Times = Times with { PlansMade = Times.PlansMade + time } };
    }
}
