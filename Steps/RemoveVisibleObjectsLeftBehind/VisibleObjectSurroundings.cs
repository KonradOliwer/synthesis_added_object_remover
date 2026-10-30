using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind;

/// <summary>A candidate of the visible left-behind step with the areas of its surroundings.</summary>
internal readonly record struct MeasuredSurroundings(int TargetIndex, SectorAreas Surroundings);

/// <summary>
/// The surroundings of the step's candidates across its rounds. Each candidate's neighbours, and its areas with every neighbour
/// standing, are measured once; an update takes in only the objects that left their place since the update before and measures
/// again only the undecided candidates around them.
/// </summary>
/// <remarks>
/// Only rounds change what became of an object, a round changes exactly the objects it took from their place, and decisions never
/// revert: so an object leaves its place at most once and keeps its fate. The removed and restored sums are added up from zero in
/// ascending neighbour id at every measurement, so the areas are bit for bit those of a full measurement under the same decisions,
/// however the removals split into rounds and whatever the number of workers.
/// </remarks>
internal sealed class VisibleObjectSurroundings
{
    /// <summary>Target indexes, ascending.</summary>
    private readonly int[] _candidates;

    /// <summary>By position in <see cref="_candidates"/>.</summary>
    private readonly CandidateNeighbours[] _neighbours;

    /// <summary>By target index: the positions in <see cref="_candidates"/> of the candidates that have it as a neighbour.</summary>
    private readonly InvertedLists _candidatesAround;

    /// <summary>By target index.</summary>
    private readonly BoxFate[] _fateOf;

    private readonly WorkOrder _order;
    private readonly Execution _execution;
    private int _roundsSeen;

    private VisibleObjectSurroundings(int[] candidates, CandidateNeighbours[] neighbours, int targetCount, WorkOrder order, Execution execution)
    {
        _candidates = candidates;
        _neighbours = neighbours;
        _candidatesAround = InvertedLists.Of(targetCount, [.. neighbours.Select(candidate => candidate.Targets)]);
        _fateOf = new BoxFate[targetCount];
        _order = order;
        _execution = execution;
        Initial = [.. candidates.Select((target, position) => new MeasuredSurroundings(target, neighbours[position].AllStanding.Build()))];
    }

    /// <summary>Every candidate with every other visible object standing, in target order.</summary>
    public IReadOnlyList<MeasuredSurroundings> Initial { get; }

    /// <summary>Measures each candidate's surroundings with every other visible object standing.</summary>
    /// <param name="candidates">Target indexes of visible targets.</param>
    /// <param name="index">The visible target objects, removed or not, where the rounds measure them.</param>
    public static VisibleObjectSurroundings Measure(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<int> candidates,
        IVisibleTargetObjects index,
        float lookAround,
        int directionClearedPercent,
        WorkOrder order,
        Execution execution)
    {
        int[] ascending = [.. candidates.Order()];
        var neighbours = ParallelMap.RunOver(
            execution,
            order,
            ascending,
            () => new List<VisibleTargetNear>(),
            (targetIndex, scratch) => CandidateNeighbours.Measure(targets[targetIndex], index, lookAround, directionClearedPercent, scratch),
            ParallelMap.AutomaticRangeSize);
        return new VisibleObjectSurroundings(ascending, neighbours, targets.Count, order, execution);
    }

    /// <summary>Takes in the objects that the rounds of <paramref name="decisions"/> after those of the update before took from their place, with their fates there.</summary>
    /// <param name="decisions">A later view of the same decisions as at the update before.</param>
    /// <returns>The still-undecided candidates with one of those objects as a neighbour, measured again, in target order.</returns>
    public IReadOnlyList<MeasuredSurroundings> Update(IRemovalDecisions decisions)
    {
        var changed = TakeInLeftTheirPlace(decisions, TakeRoundsSinceLastUpdate(decisions));
        return MeasureAgain(changed);
    }

    private ImmutableArray<Round> TakeRoundsSinceLastUpdate(IRemovalDecisions decisions)
    {
        var rounds = decisions.Rounds[_roundsSeen..];
        _roundsSeen += rounds.Length;
        return rounds;
    }

    /// <returns>The target indexes of the undecided candidates whose neighbours changed, ascending.</returns>
    private int[] TakeInLeftTheirPlace(IRemovalDecisions decisions, ImmutableArray<Round> rounds)
    {
        var changed = new SortedSet<int>();
        foreach (var gone in rounds.SelectMany(round => decisions.LeftIn(round)))
        {
            RecordFate(gone, NeighbourFates.Of(decisions, gone));
            foreach (var position in _candidatesAround.NodesListing(gone.Index))
            {
                var candidate = _candidates[position];
                if (decisions.IsDecided(new TargetId(candidate))) continue;
                _neighbours[position].AddGone(gone.Index);
                changed.Add(candidate);
            }
        }
        return [.. changed];
    }

    private void RecordFate(TargetId gone, BoxFate fate)
    {
        if (_fateOf[gone.Index] != BoxFate.Stands) throw new InvalidOperationException($"Target {gone.Index} left its place twice.");
        _fateOf[gone.Index] = fate;
    }

    private MeasuredSurroundings[] MeasureAgain(int[] candidates) =>
        ParallelMap.RunOver(
            _execution,
            _order,
            candidates,
            static () => (object?)null,
            (targetIndex, _) => new MeasuredSurroundings(targetIndex, _neighbours[Array.BinarySearch(_candidates, targetIndex)].WithGone(_fateOf)),
            ParallelMap.AutomaticRangeSize);

    /// <summary>A candidate's visible neighbours in ascending target id, what each adds to its directions, and which of them left their place.</summary>
    private sealed class CandidateNeighbours
    {
        private readonly SectorFootprint[] _footprints;

        /// <summary>Positions in <see cref="Targets"/>, ascending.</summary>
        private readonly List<int> _gone = [];

        private CandidateNeighbours(int[] targets, SectorFootprint[] footprints, SectorAreaTally allStanding)
        {
            Targets = targets;
            _footprints = footprints;
            AllStanding = allStanding;
        }

        /// <summary>Target indexes, ascending.</summary>
        public int[] Targets { get; }

        public SectorAreaTally AllStanding { get; }

        public static CandidateNeighbours Measure(
            TargetObject candidate, IVisibleTargetObjects index, float lookAround, int directionClearedPercent, List<VisibleTargetNear> scratch)
        {
            var point = candidate.Transform.Position;
            index.Around(candidate.SpaceKey, point, lookAround, scratch);
            // The neighbours come in ascending target id, which fixes the order of the float sums.
            VisibleTargetNear[] neighbours = [.. scratch.Where(neighbour => neighbour.Target != candidate.Id)];
            SectorFootprint[] footprints = [.. neighbours.Select(neighbour => SectorFootprint.Of(point, neighbour.Box))];
            var allStanding = new SectorAreaTally(directionClearedPercent);
            foreach (var footprint in footprints) allStanding.Add(footprint, BoxFate.Stands);
            return new CandidateNeighbours([.. neighbours.Select(neighbour => neighbour.Target.Index)], footprints, allStanding);
        }

        public void AddGone(int target)
        {
            var position = Array.BinarySearch(Targets, target);
            _gone.Insert(~_gone.BinarySearch(position), position);
        }

        public SectorAreas WithGone(BoxFate[] fateOf) =>
            AllStanding.WithGone(_gone.Select(position => (_footprints[position], fateOf[Targets[position]])));
    }
}
