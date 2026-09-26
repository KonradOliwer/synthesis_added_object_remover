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

    /// <summary>Written only by some follow-up steps, so a file an earlier run left behind would look current.</summary>
    private static readonly string[] FollowUpDiagnosticsFiles =
    [
        AnchoringDiagnosticsWriter.FileName,
        TouchDiagnosticsWriter.EdgesFileName,
        TouchDiagnosticsWriter.ComponentsFileName,
        OrphanDiagnosticsWriter.FileName,
    ];

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
        _meshMessages = new MeshMessageLog(config.DetailedLog);
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
        DeleteEarlierFollowUpDiagnostics();
        WriteMeshOrigins(scan.Targets);
        MarkReplacedObjects(scan.Targets, indexes);

        var keepRule = new KeepReferencedRule(scan.TargetReferences);
        var tooClose = SelectTooCloseRemovals(scan, indexes, keepRule);
        var followUp = SelectFollowUpRemovals(scan, tooClose, keepRule);
        var orphans = SelectOrphanRemovals(scan, [.. tooClose.Removals, .. followUp.Removals], keepRule);

        List<Removal> removals = [.. tooClose.Removals, .. followUp.Removals, .. orphans.Removals];
        WriteOverrides(scan, removals);

        var keptCount = tooClose.Kept.Count + followUp.Kept.Count + orphans.Kept.Count;
        PrintFinalReport(scan, indexes, removals, tooClose.Removals.Count, orphans.Removals.Count, keptCount);
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

    /// <summary>Resolves every target base's bounds in parallel.</summary>
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

    private void DeleteEarlierFollowUpDiagnostics()
    {
        if (!_config.WritesDiagnostics) return;

        AccessDiagnosticsFolder("deleting earlier follow-up diagnostics files", () =>
        {
            foreach (var fileName in FollowUpDiagnosticsFiles) CsvFile.DeleteIfPresent(Path.Combine(_config.DiagnosticsFolder, fileName));
        });
    }

    private void WriteMeshOrigins(IReadOnlyList<TargetObject> targets)
    {
        if (!_config.WritesDiagnostics) return;

        var rows = MeshOriginDiagnosticsWriter.CreateRows(targets, _shapes, _parallelOptions);
        _meshMessages.PrintAndClear();
        RunReport.PrintMeshOriginSummary(MeshOriginDiagnosticsWriter.Summarize(rows));
        AccessDiagnosticsFolder($"writing {MeshOriginDiagnosticsWriter.FileName}", () =>
        {
            var path = MeshOriginDiagnosticsWriter.Write(_config.DiagnosticsFolder, rows);
            Console.WriteLine($"  Wrote {rows.Count:N0} mesh origins to {path}.");
        });
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
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        KeepReferencedRule keepRule)
    {
        var timer = Stopwatch.StartNew();
        var hits = TooCloseSearch.FindTooCloseTargets(scan.Targets, indexes, _shapes, _config.SizeMultiplier, _parallelOptions);
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
        KeepReferencedRule keepRule)
    {
        if (tooClose.Removals.Count == 0) return FollowUpRemovals.None;
        return _config.FollowUpMode switch
        {
            FollowUpRemovalMode.Off => FollowUpRemovals.None,
            FollowUpRemovalMode.AnyTouch => RemoveTouchingClusters(scan, tooClose, keepRule),
            FollowUpRemovalMode.Anchoring => RemoveUnanchoredObjects(scan, tooClose, keepRule),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {_config.FollowUpMode}."),
        };
    }

    private FollowUpRemovals RemoveTouchingClusters(ScanResult scan, TooCloseSelection tooClose, KeepReferencedRule keepRule)
    {
        var clusters = TouchClusterFinder.Find(
            scan.Targets,
            tooClose.SeedIndices,
            tooClose.KeptIndices,
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

        AccessDiagnosticsFolder($"writing {TouchDiagnosticsWriter.EdgesFileName} / {TouchDiagnosticsWriter.ComponentsFileName}", () =>
        {
            var timer = Stopwatch.StartNew();
            var written = TouchDiagnosticsWriter.Write(_config.DiagnosticsFolder, scan, _shapes, _config.TouchDistance, seeds, clusters, diagnostics);
            Console.WriteLine($"Touch diagnostics: wrote {written.EdgeCount:N0} edges, {written.ComponentCount:N0} components in {timer.Elapsed.TotalSeconds:F1}s "
                + $"to {written.EdgesPath} / {written.ComponentsPath}.");
        });
    }

    private FollowUpRemovals RemoveUnanchoredObjects(ScanResult scan, TooCloseSelection tooClose, KeepReferencedRule keepRule)
    {
        var supporters = new SupporterIndex(scan.SupportersBySpace, _shapes);
        var anchoring = AnchoringRemover.Run(
            scan.Targets,
            tooClose.SeedIndices,
            tooClose.KeptIndices,
            supporters,
            new TerrainHeights(scan.Landscapes, scan.LandWorldspaces),
            _shapes,
            keepRule,
            _config.TouchDistance,
            _config.AnchoringThreshold,
            _parallelOptions);
        RunReport.PrintKept(scan, anchoring.Kept);
        _meshMessages.PrintAndClear();
        RunReport.PrintAnchoringStats(anchoring);
        WriteAnchoringDiagnostics(scan, supporters, anchoring);
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

    private OrphanResult SelectOrphanRemovals(ScanResult scan, IReadOnlyList<Removal> earlierRemovals, KeepReferencedRule keepRule)
    {
        if (!_config.RemoveOrphanedInvisibleObjects) return OrphanResult.None;

        var timer = Stopwatch.StartNew();
        var remover = new OrphanedInvisibleObjectRemover(
            scan.Targets, _shapes, keepRule, _config.OrphanCheckRadius, _config.OrphanRemovedShare);
        var orphans = remover.Run(earlierRemovals.Select(removal => removal.TargetIndex).ToHashSet(), _parallelOptions);
        RunReport.PrintKept(scan, orphans.Kept);
        _meshMessages.PrintAndClear();
        RunReport.PrintOrphanStats(orphans, timer.Elapsed);
        WriteOrphanDiagnostics(scan, orphans);
        return orphans;
    }

    private void WriteOrphanDiagnostics(ScanResult scan, OrphanResult orphans)
    {
        if (!_config.WritesDiagnostics) return;

        AccessDiagnosticsFolder($"writing {OrphanDiagnosticsWriter.FileName}", () =>
        {
            var path = OrphanDiagnosticsWriter.Write(_config.DiagnosticsFolder, scan, _shapes, orphans.Evaluations, _config.OrphanRemovedShare);
            Console.WriteLine($"Orphan diagnostics: wrote {orphans.Evaluations.Count:N0} evaluations to {path}.");
        });
    }

    /// <summary>Diagnostics files never change the results, so failing to access them is only a warning.</summary>
    private void AccessDiagnosticsFolder(string description, Action access)
    {
        try
        {
            access();
        }
        catch (Exception ex) when (ExpectedFailures.IsFileAccess(ex))
        {
            Console.WriteLine($"  Warning: {description} in {_config.DiagnosticsFolder} failed: {ex.Message}");
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
        int removedOrphans,
        int keptAsReferenced)
    {
        if (_config.DetailedLog) RunReport.PrintRemovals(scan, _shapes, removals);
        RunReport.PrintBoundsStats(_shapes.GetStats());
        if (_config.DetailedLog) RunReport.PrintSpaceSummary(scan, indexes, removals);
        RunReport.PrintRemovalSummary(removals.Count, removedTooClose, removedOrphans, keptAsReferenced);
    }
}
