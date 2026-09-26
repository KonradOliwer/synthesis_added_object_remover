using System.Collections.Concurrent;
using System.Diagnostics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

internal sealed record TouchStats(
    int Components,
    int ComponentsWithTouching,
    int LargestComponent,
    int CandidatePairs,
    int TouchingPairs,
    int PairsWithoutGeometry,
    VoxelCacheStats Voxels,
    TimeSpan Setup,
    TimeSpan BroadPhase,
    TimeSpan NarrowPhase,
    TimeSpan Clusters);

internal sealed record TouchClusters(List<TouchingRemoval> Removals, List<KeptTarget> Kept, TouchStats Stats);

/// <summary>
/// Connected components of the "touches" graph (target objects of one space) that contain a
/// too-close removal. BFS by level; each level's broad and narrow phases run in parallel and
/// merge in frontier order (deterministic). Touching is decided from mesh triangles only, so a
/// pair where either object has no mesh geometry never touches. Referenced objects stay and do
/// not propagate.
/// </summary>
internal sealed class TouchClusterFinder
{
    private enum PairTest { NoGeometry, Apart, Touching }

    /// <summary>One BFS level's candidate pairs, grouped by the node they reach (groups and pairs in frontier order).</summary>
    private sealed record CandidateLevel(List<(int From, int To)> Pairs, List<List<int>> PairsByTo);

    private readonly IReadOnlyList<TargetObject> _targets;
    private readonly BaseObjectShapeProvider _shapes;
    private readonly KeepReferencedRule _keepRule;
    private readonly float _tolerance;
    private readonly ParallelOptions _parallelOptions;
    private readonly VoxelCache _voxels;

    private readonly OrientedBox[] _boxes;
    private readonly Dictionary<FormKey, (SpatialGrid Grid, List<int> Members)> _grids = new();
    private readonly bool[] _isSeed;
    private readonly bool[] _visited;

    private readonly List<TouchingRemoval> _removals = [];
    private readonly List<KeptTarget> _kept = [];
    private readonly Stopwatch _broadTimer = new();
    private readonly Stopwatch _narrowTimer = new();
    private int _components;
    private int _componentsWithTouching;
    private int _largestComponent;
    private int _candidatePairs;
    private int _touchingPairs;
    private int _pairsWithoutGeometry;

    private TouchClusterFinder(
        IReadOnlyList<TargetObject> targets,
        BaseObjectShapeProvider shapes,
        KeepReferencedRule keepRule,
        float tolerance,
        float voxelSize,
        ParallelOptions parallelOptions)
    {
        _targets = targets;
        _shapes = shapes;
        _keepRule = keepRule;
        _tolerance = tolerance;
        _parallelOptions = parallelOptions;
        _voxels = new VoxelCache(voxelSize, shapes.ReadGeometry);
        _boxes = new OrientedBox[targets.Count];
        _isSeed = new bool[targets.Count];
        _visited = new bool[targets.Count];
    }

    /// <param name="seeds">Target indices of the too-close removals, in removal order.</param>
    /// <param name="keptTooClose">Too-close targets kept as referenced: already logged and counted, never propagated.</param>
    public static TouchClusters Find(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<int> seeds,
        IReadOnlyList<int> keptTooClose,
        BaseObjectShapeProvider shapes,
        KeepReferencedRule keepRule,
        float tolerance,
        float voxelSize,
        ParallelOptions parallelOptions)
    {
        var setupTimer = Stopwatch.StartNew();
        var finder = new TouchClusterFinder(targets, shapes, keepRule, tolerance, voxelSize, parallelOptions);
        finder.BuildIndex(seeds.Select(seed => targets[seed].SpaceKey).ToHashSet());
        var setup = setupTimer.Elapsed;

        foreach (var seed in seeds) finder._isSeed[seed] = true;
        foreach (var index in keptTooClose) finder._visited[index] = true;
        return finder.ExploreComponents(seeds, setup);
    }

    /// <summary>Oriented boxes and per-space grids of grown world AABBs, only for spaces with seeds.</summary>
    private void BuildIndex(HashSet<FormKey> seedSpaces)
    {
        Parallel.ForEach(Partitioner.Create(0, _targets.Count), _parallelOptions, range =>
        {
            for (var i = range.Item1; i < range.Item2; i++)
            {
                if (!seedSpaces.Contains(_targets[i].SpaceKey)) continue;
                _boxes[i] = OrientedBox.FromLocal(_shapes.GetLocalBox(_targets[i].Base), _targets[i].Transform);
            }
        });

        var membersBySpace = new Dictionary<FormKey, List<int>>();
        for (var i = 0; i < _targets.Count; i++)
        {
            if (!seedSpaces.Contains(_targets[i].SpaceKey)) continue;
            if (!membersBySpace.TryGetValue(_targets[i].SpaceKey, out var members))
            {
                members = [];
                membersBySpace[_targets[i].SpaceKey] = members;
            }
            members.Add(i);
        }
        foreach (var (spaceKey, members) in membersBySpace)
        {
            var aabbs = members.Select(i => _boxes[i].WorldAabb(_tolerance)).ToArray();
            _grids[spaceKey] = (SpatialGrid.FromBoxes(aabbs), members);
        }
    }

    private TouchClusters ExploreComponents(IReadOnlyList<int> seeds, TimeSpan setup)
    {
        var clusterTimer = Stopwatch.StartNew();
        foreach (var seed in seeds)
        {
            // A seed reached from an earlier seed already belongs to that seed's component.
            if (_visited[seed]) continue;
            _visited[seed] = true;

            var componentSize = ExploreComponent(seed);
            _components++;
            if (componentSize > 1) _componentsWithTouching++;
            _largestComponent = Math.Max(_largestComponent, componentSize);
        }
        clusterTimer.Stop();

        var stats = new TouchStats(
            _components,
            _componentsWithTouching,
            _largestComponent,
            _candidatePairs,
            _touchingPairs,
            _pairsWithoutGeometry,
            _voxels.GetStats(),
            setup,
            _broadTimer.Elapsed,
            _narrowTimer.Elapsed,
            clusterTimer.Elapsed - _broadTimer.Elapsed - _narrowTimer.Elapsed);
        return new TouchClusters(_removals, _kept, stats);
    }

    /// <returns>Number of removed objects in the component, the seed included.</returns>
    private int ExploreComponent(int seed)
    {
        var componentSize = 1;
        var frontier = new List<int> { seed };
        while (frontier.Count > 0)
        {
            var level = FindCandidatePairs(frontier);
            _candidatePairs += level.Pairs.Count;
            var touchingPairs = FindFirstTouchingPairPerTo(level);
            frontier = MergeLevel(level, touchingPairs, ref componentSize);
        }
        return componentSize;
    }

    /// <summary>Broad phase: unvisited targets whose grown oriented box overlaps a frontier node's box.</summary>
    private CandidateLevel FindCandidatePairs(List<int> frontier)
    {
        _broadTimer.Start();
        var candidates = new List<int>[frontier.Count];
        Parallel.For(
            0,
            frontier.Count,
            _parallelOptions,
            () => (Scratch: new List<int>(), Seen: new HashSet<int>()),
            (f, _, local) =>
            {
                candidates[f] = FindOverlappingUnvisited(frontier[f], local.Scratch, local.Seen);
                return local;
            },
            _ => { });

        var level = GroupPairsByTo(frontier, candidates);
        _broadTimer.Stop();
        return level;
    }

    /// <returns>Sorted target indices.</returns>
    private List<int> FindOverlappingUnvisited(int node, List<int> scratch, HashSet<int> seen)
    {
        var (grid, members) = _grids[_targets[node].SpaceKey];
        scratch.Clear();
        seen.Clear();
        grid.Collect(_boxes[node].WorldAabb(_tolerance), scratch);
        var found = new List<int>();
        foreach (var slot in scratch)
        {
            var other = members[slot];
            if (other == node || _visited[other] || !seen.Add(other)) continue;
            if (_boxes[node].Intersects(_boxes[other], _tolerance)) found.Add(other);
        }
        found.Sort();
        return found;
    }

    private static CandidateLevel GroupPairsByTo(List<int> frontier, List<int>[] candidates)
    {
        var pairs = new List<(int From, int To)>();
        var groups = new List<List<int>>();
        var groupOf = new Dictionary<int, int>();
        for (var f = 0; f < frontier.Count; f++)
        {
            foreach (var to in candidates[f])
            {
                if (!groupOf.TryGetValue(to, out var group))
                {
                    group = groups.Count;
                    groupOf[to] = group;
                    groups.Add([]);
                }
                groups[group].Add(pairs.Count);
                pairs.Add((frontier[f], to));
            }
        }
        return new CandidateLevel(pairs, groups);
    }

    /// <summary>Narrow phase: per reached node, the first pair (in pair order) whose meshes touch, or -1.</summary>
    private int[] FindFirstTouchingPairPerTo(CandidateLevel level)
    {
        _narrowTimer.Start();
        var touchingPair = new int[level.PairsByTo.Count];
        Parallel.For(0, level.PairsByTo.Count, _parallelOptions, g =>
        {
            touchingPair[g] = -1;
            foreach (var k in level.PairsByTo[g])
            {
                var (from, to) = level.Pairs[k];
                var result = TestPair(from, to);
                if (result == PairTest.NoGeometry) Interlocked.Increment(ref _pairsWithoutGeometry);
                if (result != PairTest.Touching) continue;
                touchingPair[g] = k;
                break;
            }
        });
        _narrowTimer.Stop();
        return touchingPair;
    }

    private PairTest TestPair(int from, int to)
    {
        var fromPath = _shapes.GetMeshPath(_targets[from].Base);
        var toPath = _shapes.GetMeshPath(_targets[to].Base);
        var fromMesh = fromPath == null ? null : _voxels.Get(fromPath);
        var toMesh = fromMesh == null || toPath == null ? null : _voxels.Get(toPath);
        if (fromMesh == null || toMesh == null) return PairTest.NoGeometry;

        // Sample the mesh with fewer voxels (cheaper); look up in the other.
        var touches = fromMesh.VoxelCount >= toMesh.VoxelCount
            ? VoxelMesh.Touches(fromMesh, _targets[from].Transform, toMesh, _targets[to].Transform, _tolerance)
            : VoxelMesh.Touches(toMesh, _targets[to].Transform, fromMesh, _targets[from].Transform, _tolerance);
        return touches ? PairTest.Touching : PairTest.Apart;
    }

    /// <summary>Applies the level's touching pairs in pair order and returns the next frontier.</summary>
    private List<int> MergeLevel(CandidateLevel level, int[] touchingPairPerTo, ref int componentSize)
    {
        var next = new List<int>();
        foreach (var k in touchingPairPerTo.Where(k => k >= 0).Order())
        {
            _touchingPairs++;
            var (from, to) = level.Pairs[k];
            if (_visited[to]) continue;
            _visited[to] = true;

            if (_isSeed[to])
            {
                // Another too-close removal: same component, already removed.
                componentSize++;
                next.Add(to);
                continue;
            }

            if (_keepRule.TryGetKeepReason(_targets[to], out var keepReason))
            {
                _kept.Add(new KeptTarget(to, keepReason, TouchedRemovedIndex: from));
                continue;
            }

            _removals.Add(new TouchingRemoval(to, TouchedTargetIndex: from));
            componentSize++;
            next.Add(to);
        }
        return next;
    }
}
