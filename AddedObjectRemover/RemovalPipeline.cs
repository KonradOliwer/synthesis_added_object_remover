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
    private sealed record TooCloseSelection(List<TooCloseRemoval> Removals, List<KeptTarget> Kept);

    private sealed record FollowUpRemovals(IReadOnlyList<Removal> Removals, IReadOnlyList<KeptTarget> Kept)
    {
        public static FollowUpRemovals None { get; } = new([], []);
    }

    private readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> _state;
    private readonly RunConfig _config;
    private readonly ParallelOptions _parallelOptions = new() { MaxDegreeOfParallelism = Environment.ProcessorCount };
    private readonly MeshMessageLog _meshMessages;
    private readonly BaseObjectShapeProvider _shapes;
    private readonly ReasonCounter _invisibleOthers = new();

    public RemovalPipeline(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, RunConfig config)
    {
        _state = state;
        _config = config;
        _meshMessages = new MeshMessageLog(config.Verbose);
        var meshFiles = new MeshFileSource(
            state.DataFolderPath.Path,
            state.GameRelease,
            state.LoadOrder.ListedOrder.Select(listing => listing.ModKey).ToList(),
            _meshMessages);
        _shapes = new BaseObjectShapeProvider(state.LinkCache, meshFiles, _meshMessages);
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
        WriteMeshOrigins(scan.Targets);
        MarkReplacedObjects(scan.Targets, indexes);

        var keepRule = new KeepReferencedRule(scan.TargetReferences);
        var tooClose = SelectTooCloseRemovals(scan, indexes, keepRule);
        var followUp = SelectFollowUpRemovals(scan, indexes, tooClose, keepRule);

        List<Removal> removals = [.. tooClose.Removals, .. followUp.Removals];
        WriteOverrides(scan, removals);

        var keptCount = tooClose.Kept.Count + followUp.Kept.Count;
        PrintFinalReport(scan, indexes, removals, tooClose.Removals.Count, keptCount);
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

    /// <summary>
    /// Resolves every target base's bounds in parallel. Other objects' bounds are resolved lazily,
    /// only for objects a query turns up.
    /// </summary>
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

    private void WriteMeshOrigins(IReadOnlyList<TargetObject> targets)
    {
        if (!_config.WritesDiagnostics) return;

        var rows = MeshOriginReport.CreateRows(targets, _shapes);
        RunReport.PrintMeshOriginSummary(MeshOriginReport.Summarize(rows));
        WriteDiagnosticsFile(MeshOriginReport.FileName, () =>
        {
            var path = MeshOriginReport.Write(_config.DiagnosticsFolder, rows);
            Console.WriteLine($"  Wrote {rows.Count:N0} mesh origins to {path}.");
        });
    }

    private void MarkReplacedObjects(IReadOnlyList<TargetObject> targets, IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes)
    {
        var timer = Stopwatch.StartNew();
        var matcher = new ReplacementMatcher(
            targets, indexes, _shapes, RunConfig.ReplacementPositionTolerance, RunConfig.ReplacementSizeSimilarity);
        var result = matcher.MarkReplacedObjects(collectLog: _config.Verbose, _parallelOptions);
        RunReport.PrintReplacementLog(result.LogEntries);
        _meshMessages.PrintAndClear();
        RunReport.PrintReplacementSummary(result.ReplacedCount, timer.Elapsed);
    }

    private TooCloseSelection SelectTooCloseRemovals(
        ScanResult scan,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        KeepReferencedRule keepRule)
    {
        var timer = Stopwatch.StartNew();
        var hits = TooCloseSearch.FindTooCloseTargets(scan.Targets, indexes, _shapes, _config.Multiplier, _parallelOptions);
        _meshMessages.PrintAndClear();
        RunReport.PrintTooCloseSummary(hits.Count, scan.Targets.Count, _config.Target, timer.Elapsed);
        RunReport.PrintInvisibleOthers(_invisibleOthers, _config.Verbose);

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
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        TooCloseSelection tooClose,
        KeepReferencedRule keepRule)
    {
        if (tooClose.Removals.Count == 0) return FollowUpRemovals.None;
        return _config.FollowUpMode switch
        {
            FollowUpRemovalMode.Off => FollowUpRemovals.None,
            FollowUpRemovalMode.AnyTouch => RemoveTouchingClusters(scan, tooClose, keepRule),
            FollowUpRemovalMode.Anchoring => RemoveUnanchoredObjects(scan, indexes, tooClose, keepRule),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {_config.FollowUpMode}."),
        };
    }

    private FollowUpRemovals RemoveTouchingClusters(ScanResult scan, TooCloseSelection tooClose, KeepReferencedRule keepRule)
    {
        var clusters = TouchClusterFinder.Find(
            scan.Targets,
            tooClose.Removals.Select(r => r.TargetIndex).ToList(),
            tooClose.Kept.Select(k => k.TargetIndex).ToList(),
            _shapes,
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

        WriteDiagnosticsFile("touch diagnostics", () =>
        {
            var timer = Stopwatch.StartNew();
            var written = TouchDiagnosticsWriter.Write(_config.DiagnosticsFolder, scan, _shapes, _config.TouchDistance, seeds, clusters, diagnostics);
            Console.WriteLine($"Touch diagnostics: wrote {written.EdgeCount:N0} edges, {written.ComponentCount:N0} components in {timer.Elapsed.TotalSeconds:F1}s "
                + $"to {written.EdgesPath} / {written.ComponentsPath}.");
        });
    }

    private FollowUpRemovals RemoveUnanchoredObjects(
        ScanResult scan,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        TooCloseSelection tooClose,
        KeepReferencedRule keepRule)
    {
        var anchoring = AnchoringRemover.Run(
            scan.Targets,
            tooClose.Removals.Select(r => r.TargetIndex).ToList(),
            tooClose.Kept.Select(k => k.TargetIndex).ToList(),
            indexes,
            new TerrainHeights(scan.Landscapes),
            _shapes,
            keepRule,
            _config.TouchDistance,
            _config.AnchoringThreshold,
            _parallelOptions);
        RunReport.PrintKept(scan, anchoring.Kept);
        _meshMessages.PrintAndClear();
        RunReport.PrintAnchoringStats(anchoring);
        WriteAnchoringDiagnostics(scan, indexes, anchoring);
        return new FollowUpRemovals(anchoring.Removals, anchoring.Kept);
    }

    private void WriteAnchoringDiagnostics(ScanResult scan, IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes, AnchoringResult anchoring)
    {
        if (!_config.WritesDiagnostics) return;

        WriteDiagnosticsFile(AnchoringDiagnosticsWriter.FileName, () =>
        {
            var path = AnchoringDiagnosticsWriter.Write(
                _config.DiagnosticsFolder, scan, _shapes, indexes, anchoring.Evaluations, _config.AnchoringThreshold);
            Console.WriteLine($"Anchoring diagnostics: wrote {anchoring.Evaluations.Count:N0} evaluations to {path}.");
        });
    }

    /// <summary>A diagnostics file never changes the results, so failing to write one is only a warning.</summary>
    private void WriteDiagnosticsFile(string description, Action write)
    {
        try
        {
            write();
        }
        catch (Exception ex) when (ExpectedFailures.IsFileAccess(ex))
        {
            Console.WriteLine($"  Warning: could not write {description} to {_config.DiagnosticsFolder}: {ex.Message}");
        }
    }

    private void WriteOverrides(ScanResult scan, IReadOnlyList<Removal> removals)
    {
        var timer = Stopwatch.StartNew();
        var remover = new ObjectRemover(_state.PatchMod);
        var enableParentsReplaced = 0;
        foreach (var removal in removals)
        {
            var index = removal.TargetIndex;
            if (remover.Disable(scan.Targets[index].Record, scan.TargetLocations[index])) enableParentsReplaced++;
        }
        RunReport.PrintWriteSummary(removals.Count, enableParentsReplaced, timer.Elapsed);
    }

    private void PrintFinalReport(
        ScanResult scan,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        IReadOnlyList<Removal> removals,
        int removedTooClose,
        int keptAsReferenced)
    {
        if (_config.Verbose) RunReport.PrintRemovals(scan, _shapes, removals);
        RunReport.PrintBoundsStats(_shapes.GetStats());
        if (_config.Verbose) RunReport.PrintSpaceSummary(scan, indexes, removals);
        RunReport.PrintRemovalSummary(removals.Count, removedTooClose, keptAsReferenced);
    }
}
