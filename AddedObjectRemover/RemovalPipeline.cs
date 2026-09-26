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
        _shapes = new BaseObjectShapeProvider(state.LinkCache, meshFiles, _meshMessages, config.UseNifBounds);
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
        MarkReplacedObjects(scan.Targets, indexes);

        var keepRule = new KeepReferencedRule(_config.KeepReferencedObjects, scan.TargetReferences);
        var tooClose = SelectTooCloseRemovals(scan, indexes, keepRule);
        var touching = FindTouchingClusters(scan, tooClose, keepRule);

        List<Removal> removals = [.. tooClose.Removals, .. touching?.Removals ?? []];
        WriteOverrides(scan, removals);

        var keptCount = tooClose.Kept.Count + (touching?.Kept.Count ?? 0);
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

    private void MarkReplacedObjects(IReadOnlyList<TargetObject> targets, IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes)
    {
        if (!_config.IgnoreReplacedObjects) return;

        var timer = Stopwatch.StartNew();
        var matcher = new ReplacementMatcher(
            targets, indexes, _shapes, _config.ReplacementPositionTolerance, _config.ReplacementSizeSimilarity);
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

    /// <summary>Null when touching objects are not removed.</summary>
    private TouchClusters? FindTouchingClusters(ScanResult scan, TooCloseSelection tooClose, KeepReferencedRule keepRule)
    {
        if (!_config.RemoveTouching || tooClose.Removals.Count == 0) return null;
        if (!_config.UseNifBounds)
        {
            Console.WriteLine("Touching objects: skipped, mesh (NIF) measurement is disabled and touching needs mesh geometry.");
            return null;
        }

        var clusters = TouchClusterFinder.Find(
            scan.Targets,
            tooClose.Removals.Select(r => r.TargetIndex).ToList(),
            tooClose.Kept.Select(k => k.TargetIndex).ToList(),
            _shapes,
            keepRule,
            _config.TouchTolerance,
            _parallelOptions,
            collectDiagnostics: !string.IsNullOrEmpty(_config.TouchDiagnosticsFile));
        RunReport.PrintKept(scan, clusters.Kept);
        _meshMessages.PrintAndClear();
        RunReport.PrintTouchStats(clusters);
        WriteTouchDiagnostics(scan, tooClose.Removals, clusters);
        return clusters;
    }

    private void WriteTouchDiagnostics(ScanResult scan, IReadOnlyList<TooCloseRemoval> seeds, TouchClusters clusters)
    {
        if (clusters.Diagnostics is not { } diagnostics) return;

        var timer = Stopwatch.StartNew();
        try
        {
            var written = TouchDiagnosticsWriter.Write(_config.TouchDiagnosticsFile, scan, _shapes, _config.TouchTolerance, seeds, clusters, diagnostics);
            Console.WriteLine($"Touch diagnostics: wrote {written.EdgeCount:N0} edges, {written.ComponentCount:N0} components in {timer.Elapsed.TotalSeconds:F1}s "
                + $"to {written.EdgesPath} / {written.ComponentsPath}.");
        }
        catch (Exception ex) when (ExpectedFailures.IsFileAccess(ex))
        {
            Console.WriteLine($"  Warning: could not write touch diagnostics to {_config.TouchDiagnosticsFile}: {ex.Message}");
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
        RunReport.PrintBoundsStats(_shapes.GetStats(), _config.UseNifBounds);
        if (_config.Verbose) RunReport.PrintSpaceSummary(scan, indexes, removals);
        RunReport.PrintRemovalSummary(removals.Count, removedTooClose, keptAsReferenced);
    }
}
