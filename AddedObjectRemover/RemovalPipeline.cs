using System.Diagnostics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover;

/// <summary>
/// Runs the phases of one patch run in order. All computation finishes before overrides are
/// written, single-threaded, into the patch.
/// </summary>
internal sealed class RemovalPipeline
{
    private sealed record TooCloseSelection(List<TooCloseRemoval> Removals, List<KeptTarget> Kept)
    {
        public List<int> SeedIndices => Removals.Select(removal => removal.TargetIndex).ToList();

        public List<int> KeptIndices => Kept.Select(kept => kept.TargetIndex).ToList();
    }

    private sealed record FollowUpRemovals(IReadOnlyList<Removal> Removals, IReadOnlyList<KeptTarget> Kept)
    {
        public static FollowUpRemovals None { get; } = new([], []);
    }

    private readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> _state;
    private readonly RunConfig _config;
    private readonly ParallelOptions _parallelOptions = new() { MaxDegreeOfParallelism = Environment.ProcessorCount };
    private readonly MeshMessageLog _meshMessages;
    private readonly BaseObjectShapeProvider _shapes;
    private readonly TriangleTreeCache _meshCache;
    private readonly ObjectContainment _containment;
    private readonly ReasonCounter _invisibleOthers = new();

    public RemovalPipeline(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, RunConfig config)
    {
        _state = state;
        _config = config;
        _meshMessages = new MeshMessageLog(config.DetailedLog);
        var meshFiles = new MeshFileSource(
            state.DataFolderPath.Path,
            state.GameRelease,
            state.LoadOrder.ListedOrder.Select(listing => listing.ModKey).ToList(),
            _meshMessages);
        _shapes = new BaseObjectShapeProvider(state.LinkCache, meshFiles, _meshMessages);
        _meshCache = new TriangleTreeCache(_shapes.ReadGeometry);
        _containment = new ObjectContainment(_shapes, _meshCache);
    }

    public void Run(Stopwatch totalTimer)
    {
        var scan = ScanLoadOrder();
        if (scan.Targets.Count == 0)
        {
            Console.WriteLine("Nothing to check. No changes made.");
            return;
        }
        Console.WriteLine($"Using {_parallelOptions.MaxDegreeOfParallelism} threads.");

        var indexes = IndexOtherObjects(scan);
        WarmUpTargetBounds(scan.Targets);
        var visibility = ClassifyTargetVisibility(scan.Targets);
        MarkReplacedObjects(scan.Targets, indexes);

        var keepRule = new KeepReferencedRule(scan.TargetReferences);
        var supporters = new SupporterIndex(scan.SupportersBySpace, _shapes);
        var tooClose = SelectTooCloseRemovals(scan, visibility, indexes, keepRule);
        var followUp = SelectFollowUpRemovals(scan, tooClose, supporters, keepRule);
        List<Removal> removals = [.. tooClose.Removals, .. followUp.Removals];
        var leftovers = SelectLeftoverRemovals(scan, visibility, indexes, removals, keepRule);
        removals.AddRange(leftovers.Removals);
        var relocations = RelocateKeptMarkers(scan, visibility, supporters, removals, leftovers);
        WriteLeftoverDiagnostics(scan, leftovers, relocations);
        WriteOverrides(scan, removals, relocations.Moved);

        List<KeptTarget> kept = [.. tooClose.Kept, .. followUp.Kept, .. leftovers.Kept];
        PrintFinalReport(scan, indexes, removals, tooClose.Removals.Count, leftovers.Removals.Count, kept);
        Console.WriteLine($"Done in {totalTimer.Elapsed.TotalSeconds:F1}s.");
    }

    private ScanResult ScanLoadOrder()
    {
        var timer = Stopwatch.StartNew();
        var scan = PlacedRecordScanner.Scan(_state, _config);
        RunReport.PrintOverriddenOthers(scan, _config.Target);
        RunReport.PrintScanSummary(scan, _config.Target, timer.Elapsed);
        return scan;
    }

    /// <summary>Only spaces that contain target objects are indexed.</summary>
    private Dictionary<FormKey, OtherObjectIndex> IndexOtherObjects(ScanResult scan)
    {
        var timer = Stopwatch.StartNew();
        var indexes = OtherObjectIndex.BuildForTargetSpaces(scan, _shapes, _invisibleOthers);
        RunReport.PrintIndexSummary(indexes, timer.Elapsed);
        return indexes;
    }

    private void WarmUpTargetBounds(IReadOnlyList<TargetObject> targets)
    {
        var timer = Stopwatch.StartNew();
        _shapes.BuildArchiveIndexNow();
        var targetBases = targets
            .Select(t => t.Base)
            .OfType<BaseRef>()
            .DistinctBy(b => b.FormKey)
            .ToList();
        _shapes.MeasureBases(targetBases, _parallelOptions);
        _meshMessages.PrintAndClear();
        RunReport.PrintWarmUpSummary(targetBases.Count, timer.Elapsed);
    }

    /// <returns>Parallel to <paramref name="targets"/>.</returns>
    private ObjectVisibility[] ClassifyTargetVisibility(IReadOnlyList<TargetObject> targets)
    {
        var visibility = new ObjectVisibility[targets.Count];
        Parallel.For(0, targets.Count, _parallelOptions, index =>
        {
            var target = targets[index];
            visibility[index] = _shapes.GetVisibility(target.Base, target.IsPrimitive, target.HasMapMarker);
        });
        return visibility;
    }

    private void MarkReplacedObjects(IReadOnlyList<TargetObject> targets, IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes)
    {
        var timer = Stopwatch.StartNew();
        var matcher = new ReplacementMatcher(targets, indexes, _shapes);
        var result = matcher.MarkReplacedObjects(collectLog: _config.DetailedLog, _parallelOptions);
        RunReport.PrintReplacementLog(result.LogEntries);
        _meshMessages.PrintAndClear();
        RunReport.PrintReplacementSummary(result.ReplacedCount, timer.Elapsed);
    }

    private TooCloseSelection SelectTooCloseRemovals(
        ScanResult scan,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        KeepReferencedRule keepRule)
    {
        var timer = Stopwatch.StartNew();
        var hits = TooCloseSearch.FindTooCloseTargets(scan.Targets, visibility, indexes, _shapes, _config.SizeMultiplier, _parallelOptions);
        _meshMessages.PrintAndClear();
        RunReport.PrintTooCloseSummary(hits.Count, scan.Targets.Count, _config.Target, timer.Elapsed);
        RunReport.PrintInvisibleOthers(_invisibleOthers, _config.DetailedLog);

        var selection = SplitByKeepRule(scan.Targets, hits, keepRule);
        RunReport.PrintKept(scan, selection.Kept);
        return selection;
    }

    private static TooCloseSelection SplitByKeepRule(
        IReadOnlyList<TargetObject> targets,
        IEnumerable<TooCloseHit> hits,
        KeepReferencedRule keepRule)
    {
        var selection = new TooCloseSelection([], []);
        foreach (var hit in hits)
        {
            if (keepRule.TryGetKeepReason(targets[hit.TargetIndex], out var reason))
            {
                selection.Kept.Add(new KeptTarget(hit.TargetIndex, reason, TouchedTargetIndex: null));
            }
            else
            {
                selection.Removals.Add(new TooCloseRemoval(hit.TargetIndex, hit.TooCloseTo));
            }
        }
        return selection;
    }

    private FollowUpRemovals SelectFollowUpRemovals(
        ScanResult scan,
        TooCloseSelection tooClose,
        SupporterIndex supporters,
        KeepReferencedRule keepRule)
    {
        if (tooClose.Removals.Count == 0) return FollowUpRemovals.None;
        return _config.FollowUpMode switch
        {
            FollowUpRemovalMode.Off => FollowUpRemovals.None,
            FollowUpRemovalMode.AnyTouch => SelectTouchingRemovals(scan, tooClose, keepRule),
            FollowUpRemovalMode.Anchoring => SelectUnanchoredRemovals(scan, tooClose, supporters, keepRule),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {_config.FollowUpMode}."),
        };
    }

    private FollowUpRemovals SelectTouchingRemovals(ScanResult scan, TooCloseSelection tooClose, KeepReferencedRule keepRule)
    {
        var clusters = TouchClusterFinder.Find(
            scan.Targets,
            tooClose.SeedIndices,
            tooClose.KeptIndices,
            _shapes,
            _meshCache,
            keepRule,
            _config.TouchDistance,
            _parallelOptions,
            collectDiagnostics: _config.WritesDiagnostics);
        RunReport.PrintKept(scan, clusters.Kept);
        _meshMessages.PrintAndClear();
        RunReport.PrintTouchStats(clusters);
        WriteTouchDiagnostics(scan, tooClose.Removals, clusters);
        return new FollowUpRemovals(clusters.Removals, clusters.Kept);
    }

    private void WriteTouchDiagnostics(ScanResult scan, IReadOnlyList<TooCloseRemoval> seeds, TouchClusters clusters)
    {
        if (clusters.Diagnostics is not { } diagnostics) return;

        AccessDiagnosticsFolder($"writing {TouchDiagnosticsWriter.EdgesFileName} / {TouchDiagnosticsWriter.ComponentsFileName}", () =>
        {
            var timer = Stopwatch.StartNew();
            var written = TouchDiagnosticsWriter.Write(_config.DiagnosticsFolder, scan, _shapes, _config.TouchDistance, seeds, clusters, diagnostics);
            Console.WriteLine($"Touch diagnostics: wrote {written.EdgeCount:N0} edges, {written.ComponentCount:N0} components in {timer.Elapsed.TotalSeconds:F1}s "
                + $"to {written.EdgesPath} / {written.ComponentsPath}.");
        });
    }

    private FollowUpRemovals SelectUnanchoredRemovals(
        ScanResult scan,
        TooCloseSelection tooClose,
        SupporterIndex supporters,
        KeepReferencedRule keepRule)
    {
        var anchoring = AnchoringRemover.Run(
            scan.Targets,
            tooClose.SeedIndices,
            tooClose.KeptIndices,
            supporters,
            new TerrainHeights(scan.Landscapes, scan.LandWorldspaces),
            _shapes,
            _meshCache,
            keepRule,
            _config.TouchDistance,
            _config.AnchoringThreshold,
            _parallelOptions);
        RunReport.PrintKept(scan, anchoring.Kept);
        _meshMessages.PrintAndClear();
        RunReport.PrintAnchoringStats(anchoring);
        WriteAnchoringDiagnostics(scan, supporters, anchoring);
        WriteMeshOrigins(scan.Targets);
        return new FollowUpRemovals(anchoring.Removals, anchoring.Kept);
    }

    private void WriteAnchoringDiagnostics(ScanResult scan, SupporterIndex supporters, AnchoringResult anchoring)
    {
        if (!_config.WritesDiagnostics) return;

        AccessDiagnosticsFolder($"writing {AnchoringDiagnosticsWriter.FileName}", () =>
        {
            var path = AnchoringDiagnosticsWriter.Write(
                _config.DiagnosticsFolder, scan, _shapes, supporters, anchoring.Evaluations, _config.AnchoringThreshold);
            Console.WriteLine($"Anchoring diagnostics: wrote {anchoring.Evaluations.Count:N0} evaluations to {path}.");
        });
    }

    private void WriteMeshOrigins(IReadOnlyList<TargetObject> targets)
    {
        if (!_config.WritesDiagnostics) return;

        var rows = MeshOriginDiagnosticsWriter.CreateRows(targets, _shapes, _meshCache, _parallelOptions);
        _meshMessages.PrintAndClear();
        RunReport.PrintMeshOriginSummary(MeshOriginDiagnosticsWriter.Summarize(rows));
        AccessDiagnosticsFolder($"writing {MeshOriginDiagnosticsWriter.FileName}", () =>
        {
            var path = MeshOriginDiagnosticsWriter.Write(_config.DiagnosticsFolder, rows);
            Console.WriteLine($"  Wrote {rows.Count:N0} mesh origins to {path}.");
        });
    }

    private LeftoverResult SelectLeftoverRemovals(
        ScanResult scan,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        IReadOnlyList<Removal> earlierRemovals,
        KeepReferencedRule keepRule)
    {
        if (!_config.Leftovers.Enabled) return LeftoverResult.None;

        var timer = Stopwatch.StartNew();
        var selector = new LeftoverInvisibleObjectSelector(
            scan.Targets,
            visibility,
            _shapes,
            indexes,
            _containment,
            new InvisibleObjectReach(_state.LinkCache, _shapes),
            keepRule,
            _config.Leftovers);
        var leftovers = selector.SelectRemovals(ToTargetIndices(earlierRemovals), _parallelOptions);
        RunReport.PrintKept(scan, leftovers.Kept);
        _meshMessages.PrintAndClear();
        if (_config.DetailedLog) RunReport.PrintLeftoverDecisions(scan, leftovers.Evaluations);
        RunReport.PrintLeftoverStats(leftovers, timer.Elapsed);
        return leftovers;
    }

    private static HashSet<int> ToTargetIndices(IEnumerable<Removal> removals) =>
        removals.Select(removal => removal.TargetIndex).ToHashSet();

    /// <param name="removals">Every removal of the run.</param>
    private RelocationResult RelocateKeptMarkers(
        ScanResult scan,
        IReadOnlyList<ObjectVisibility> visibility,
        SupporterIndex supporters,
        IReadOnlyList<Removal> removals,
        LeftoverResult leftovers)
    {
        if (!_config.Leftovers.MovesKeptMarkers) return RelocationResult.None;

        var relocator = CreateRelocator(scan, CreateObstacles(scan, visibility, supporters, removals));
        var relocations = relocator.Relocate(leftovers.Evaluations, _parallelOptions);
        _meshMessages.PrintAndClear();
        RunReport.PrintRelocations(scan, relocations);
        return relocations;
    }

    private VisibleObstacles CreateObstacles(
        ScanResult scan,
        IReadOnlyList<ObjectVisibility> visibility,
        SupporterIndex supporters,
        IReadOnlyList<Removal> removals)
    {
        var remainingVisible = ObjectVisibility.VisibleIndices(visibility, except: ToTargetIndices(removals));
        return new VisibleObstacles(supporters, VisibleTargetIndex.Build(scan.Targets, remainingVisible, _shapes), _containment);
    }

    private static KeptObjectRelocator CreateRelocator(ScanResult scan, VisibleObstacles obstacles) => new(
        scan.Targets,
        scan.TargetLocations,
        obstacles,
        new NavmeshIndex(scan.NavmeshesBySpace),
        new TerrainSpotSearch(new TerrainHeights(scan.Landscapes, scan.LandWorldspaces)));

    private void WriteLeftoverDiagnostics(ScanResult scan, LeftoverResult leftovers, RelocationResult relocations)
    {
        if (!_config.WritesDiagnostics || !_config.Leftovers.Enabled) return;

        AccessDiagnosticsFolder($"writing {LeftoverDiagnosticsWriter.FileName}", () =>
        {
            var path = LeftoverDiagnosticsWriter.Write(_config.DiagnosticsFolder, scan, _shapes, leftovers.Evaluations, relocations);
            Console.WriteLine($"Leftover invisible objects diagnostics: wrote {leftovers.Evaluations.Count:N0} evaluations to {path}.");
        });
    }

    private void AccessDiagnosticsFolder(string description, Action access) =>
        DiagnosticsFiles.Access(_config.DiagnosticsFolder, description, access);

    private void WriteOverrides(ScanResult scan, IReadOnlyList<Removal> removals, IReadOnlyList<Relocation> moves)
    {
        var timer = Stopwatch.StartNew();
        var overrides = new PlacedOverrideWriter(_state.PatchMod);
        var remover = new ObjectRemover(overrides);
        var enableParentsReplaced = 0;
        foreach (var removal in removals)
        {
            var index = removal.TargetIndex;
            if (remover.Disable(scan.Targets[index].Record, scan.TargetLocations[index])) enableParentsReplaced++;
        }

        var mover = new ObjectMover(overrides);
        foreach (var move in moves)
        {
            var index = move.Evaluation.TargetIndex;
            mover.MoveTo(scan.Targets[index].Record, scan.TargetLocations[index], move.To);
        }
        RunReport.PrintWriteSummary(removals.Count, moves.Count, enableParentsReplaced, timer.Elapsed);
    }

    private void PrintFinalReport(
        ScanResult scan,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        IReadOnlyList<Removal> removals,
        int removedTooClose,
        int removedLeftovers,
        IReadOnlyList<KeptTarget> kept)
    {
        if (_config.DetailedLog) RunReport.PrintRemovals(scan, _shapes, removals);
        RunReport.PrintBoundsStats(_shapes.GetStats());
        if (_config.DetailedLog) RunReport.PrintSpaceSummary(scan, indexes, removals);
        RunReport.PrintRemovalSummary(removals.Count, removedTooClose, removedLeftovers);
        RunReport.PrintKeptSummary(kept);
    }
}
