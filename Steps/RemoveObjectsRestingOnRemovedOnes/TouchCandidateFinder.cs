using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes;

/// <summary>
/// Broad phase of the touch search: a target's oriented box grown by the tolerance, tested against
/// nearby targets of the same space (<see cref="NearPairFinder"/>). Targets not excluded take part. A space's
/// boxes and pair finder are built when one of its targets is first asked about. Safe to query from many threads at once.
/// </summary>
/// <remarks>
/// Pairs never cross spaces, so a target's neighbours depend only on the targets of its own space, never on which
/// spaces were asked about before or by which thread.
/// </remarks>
internal sealed class TouchCandidateFinder
{
    private const int NotTakingPart = -1;

    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly int[][] _membersOfSpace;
    private readonly int[] _spaceOf;
    private readonly int[] _slotOf;
    private readonly IBaseObjectShapes _shapes;
    private readonly float _tolerance;
    private readonly Execution _execution;
    private readonly ComputedOncePerKey<int, SpaceBoxes> _builtSpaces = new(Publication.BuiltOnce, EqualityComparer<int>.Default);

    /// <param name="membersOfSpace">Per space number, the targets taking part in it, in target order.</param>
    private TouchCandidateFinder(
        IReadOnlyList<TargetObject> targets, int[][] membersOfSpace, IBaseObjectShapes shapes, float tolerance, Execution execution)
    {
        _targets = targets;
        _membersOfSpace = membersOfSpace;
        (_spaceOf, _slotOf) = LocateMembers(targets.Count, membersOfSpace);
        _shapes = shapes;
        _tolerance = tolerance;
        _execution = execution;
    }

    /// <param name="excluded">Targets that never take part in a pair.</param>
    public static TouchCandidateFinder Create(
        IReadOnlyList<TargetObject> targets,
        bool[] excluded,
        IBaseObjectShapes shapes,
        float tolerance,
        Execution execution)
    {
        var taking = Enumerable.Range(0, targets.Count).Where(target => !excluded[target]).ToList();
        var membersOfSpace = KeyedGroups.IndicesByKey(taking, target => targets[target].SpaceKey, EqualityComparer<RecordKey>.Default)
            .Select(group => group.Indices.Select(position => taking[position]).ToArray())
            .ToArray();
        return new TouchCandidateFinder(targets, membersOfSpace, shapes, tolerance, execution);
    }

    /// <summary>World oriented box of a target taking part.</summary>
    /// <exception cref="ArgumentException">The target takes no part.</exception>
    public OrientedBox BoxOf(int target) => BuiltSpaceOf(target).Boxes[_slotOf[target]];

    /// <returns>Sorted indices of the other targets taking part whose boxes come within the tolerance of <paramref name="node"/>'s box; empty for a target that takes no part.</returns>
    public List<int> FindNeighbors(int node)
    {
        if (_spaceOf[node] == NotTakingPart) return [];

        var members = _membersOfSpace[_spaceOf[node]];
        return [.. BuiltSpaceOf(node).Pairs.NeighboursOf(_slotOf[node]).Select(slot => members[slot])];
    }

    /// <returns>Per target: the number of its space and its slot among that space's members; <see cref="NotTakingPart"/> for both when it takes no part.</returns>
    private static (int[] SpaceOf, int[] SlotOf) LocateMembers(int targetCount, int[][] membersOfSpace)
    {
        var spaceOf = new int[targetCount];
        var slotOf = new int[targetCount];
        Array.Fill(spaceOf, NotTakingPart);
        Array.Fill(slotOf, NotTakingPart);
        for (var space = 0; space < membersOfSpace.Length; space++)
        {
            for (var slot = 0; slot < membersOfSpace[space].Length; slot++)
            {
                spaceOf[membersOfSpace[space][slot]] = space;
                slotOf[membersOfSpace[space][slot]] = slot;
            }
        }
        return (spaceOf, slotOf);
    }

    private SpaceBoxes BuiltSpaceOf(int target)
    {
        var space = _spaceOf[target];
        if (space == NotTakingPart) throw new ArgumentException($"Target {target} takes no part in the touch search.", nameof(target));

        return _builtSpaces.Get(space, () => BuildSpace(_membersOfSpace[space]));
    }

    /// <remarks>
    /// The members are in target order, so slot order is target order: the neighbours stay sorted, and the pair test grows
    /// the same box of each pair as among all targets.
    /// </remarks>
    private SpaceBoxes BuildSpace(int[] members)
    {
        var boxes = ParallelMap.Run(_execution, members.Length, slot => MeasureBox(members[slot]), ParallelMap.AutomaticRangeSize);
        var space = _targets[members[0]].SpaceKey;
        return new SpaceBoxes(boxes, NearPairFinder.Create(boxes, _ => space, _tolerance, new bool[members.Length]));
    }

    private OrientedBox MeasureBox(int target) => OrientedBox.FromLocal(_shapes.Of(_targets[target].Base).Box, _targets[target].Transform);

    /// <param name="Boxes">Per slot of the space's members.</param>
    private sealed record SpaceBoxes(OrientedBox[] Boxes, NearPairFinder Pairs);
}
