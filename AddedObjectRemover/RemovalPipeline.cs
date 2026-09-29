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
        public List<int> KeptIndices => Kept.Select(kept => kept.TargetIndex).ToList();
    }

    private sealed record FollowUpRemovals(IReadOnlyList<Removal> Removals, IReadOnlyList<KeptTarget> Kept)
    {
        public static FollowUpRemovals None { get; } = new([], []);
    }

    private readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> _state;
    private readonly RunConfig _config;
    private readonly ModFacts _mods;
    private readonly ModStanding _standing;
    private readonly ParallelOptions _parallelOptions;
    private readonly MeshMessageLog _meshMessages;
    private readonly BaseObjectShapeProvider _shapes;
    private readonly TriangleTreeCache _meshCache;
    private readonly ObjectContainment _containment;
    private readonly SkinnedBodyMeasurer _bodyMeasurer;
    private readonly NpcBodyCache _npcBodies;
    private readonly ReasonCounter _invisibleOthers = new();

    public RemovalPipeline(
        IPatcherState<ISkyrimMod, ISkyrimModGetter> state, RunConfig config, ModFacts mods, ModStanding standing, int workers)
    {
        _state = state;
        _config = config;
        _mods = mods;
        _standing = standing;
        _parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = workers };
        _meshMessages = new MeshMessageLog(config.DetailedLog);
        var meshFiles = new MeshFileSource(
            state.DataFolderPath.Path,
            state.GameRelease,
            state.LoadOrder.ListedOrder.Select(listing => listing.ModKey).ToList(),
            _meshMessages);
        _shapes = new BaseObjectShapeProvider(state.LinkCache, meshFiles, _meshMessages);
        _meshCache = new TriangleTreeCache(_shapes.ReadGeometry);
        _containment = new ObjectContainment(_shapes, _meshCache);
        _bodyMeasurer = new SkinnedBodyMeasurer(_shapes.ReadGeometry);
        _npcBodies = new NpcBodyCache(new NpcBodyResolver(state.LinkCache, _shapes, _bodyMeasurer));
    }

    public void Run(Stopwatch totalTimer)
    {
        var snapshot = ScanLoadOrder();
        var world = snapshot.World;
        if (world.Targets.Length == 0)
        {
            Console.WriteLine("Nothing to check. No changes made.");
            return;
        }
        Console.WriteLine($"Using {_parallelOptions.MaxDegreeOfParallelism} threads.");

        var indexes = IndexOtherObjects(world);
        WarmUpTargetBounds(world.Targets);
        var visibility = ClassifyTargetVisibility(world.Targets);
        RunReport.PrintTargetVisibility(visibility, world.Counts.TargetsHiddenOrWithoutPlacement);
        var replacements = FindReplacements(world, indexes);

        var protection = Protection.Build(world.Targets, world.Links, world.References);
        RunReport.PrintLinkedGroups(protection.Groups, world.Counts.TargetPluginLinks);
        var supporters = new SupporterIndex(GroupSupportersBySpace(world), _shapes, _parallelOptions);
        var tooClose = SelectTooCloseRemovals(world, visibility, indexes, replacements, protection);
        List<Removal> removals = [.. tooClose.Removals];
        AddLinkedRemovals(removals, tooClose.Removals, protection.Groups, "too-close");
        var followUp = SelectFollowUpRemovals(snapshot, visibility, removals.ToList(), tooClose, supporters, protection);
        removals.AddRange(followUp.Removals);
        RunReport.PrintLinkedRemovals(followUp.Removals.Count(removal => removal is LinkedRemoval), "follow-up");
        var leftovers = SelectLeftoverRemovals(world, visibility, indexes, replacements, removals, protection);
        removals.AddRange(leftovers.Removals);
        AddLinkedRemovals(removals, leftovers.Removals, protection.Groups, "leftover invisible object");
        var relocations = RelocateKeptMarkers(snapshot, visibility, supporters, removals, leftovers);
        WriteLeftoverDiagnostics(world, leftovers, relocations);
        WriteOverrides(snapshot, removals, relocations.Moved);
        RunReport.PrintBoundsIndexTimes(indexes.Values, supporters.GetIndexedSpaces());

        List<KeptTarget> kept = [.. tooClose.Kept, .. followUp.Kept, .. leftovers.Kept];
        PrintFinalReport(world, indexes, removals, kept);
        ReportManualPatchHints(world, visibility, removals, kept, new ManualPatchHints(world.Targets, visibility, protection));
        Console.WriteLine($"Done in {totalTimer.Elapsed.TotalSeconds:F1}s.");
    }

    /// <summary>Removes the rest of the linked groups of one step's removals, so the next steps see them removed.</summary>
    /// <param name="removals">Every removal so far, <paramref name="decided"/> included; the linked removals are appended.</param>
    private static void AddLinkedRemovals(List<Removal> removals, IEnumerable<Removal> decided, LinkedGroups groups, string step)
    {
        var linked = groups.CollectLinkedRemovals(decided, ToTargetIndices(removals));
        removals.AddRange(linked);
        RunReport.PrintLinkedRemovals(linked.Count, step);
    }

    private GameSnapshot ScanLoadOrder()
    {
        var timer = Stopwatch.StartNew();
        var snapshot = PlacedRecordScanner.Scan(_state, _config, _mods.Mods, _standing);
        RunReport.PrintOverriddenOthers(snapshot.World, _config.Target);
        RunReport.PrintScanSummary(snapshot.World, _config.Target, timer.Elapsed);
        return snapshot;
    }

    /// <summary>Empty unless the run collected the objects of any plugin as supporters or obstacles.</summary>
    private static Dictionary<FormKey, List<OtherObject>> GroupSupportersBySpace(World world) =>
        world.Backdrop.IsCollected
            ? world.RivalsAndBackdrop.GroupBy(other => other.SpaceKey).ToDictionary(group => group.Key, group => group.ToList())
            : new Dictionary<FormKey, List<OtherObject>>();

    /// <summary>Only spaces that contain target objects are indexed.</summary>
    private Dictionary<FormKey, OtherObjectIndex> IndexOtherObjects(World world)
    {
        var timer = Stopwatch.StartNew();
        var indexes = OtherObjectIndex.BuildForTargetSpaces(world, _shapes, _invisibleOthers, _parallelOptions);
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

    private Replacements FindReplacements(World world, IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes)
    {
        var timer = Stopwatch.StartNew();
        var replacements = ReplacementMatcher.Find(world.Targets, indexes, world.Rivals.Length, _shapes, _parallelOptions);
        if (_config.DetailedLog) RunReport.PrintReplacementLog(world, replacements);
        _meshMessages.PrintAndClear();
        RunReport.PrintReplacementSummary(replacements.Count, timer.Elapsed);
        return replacements;
    }

    private TooCloseSelection SelectTooCloseRemovals(
        World world,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        Replacements replacements,
        Protection protection)
    {
        var timer = Stopwatch.StartNew();
        var npcRule = CreateNpcClashRule(world, visibility, indexes, replacements);
        var hits = _config.ZoneShape switch
        {
            ZoneShape.BoundingBox => TooCloseSearch.FindTooCloseTargets(
                world.Targets, visibility, indexes, replacements, _shapes, _config.SizeMultiplier, npcRule, _parallelOptions),
            ZoneShape.ObjectShape => FindShapeZoneHits(world, visibility, indexes, replacements, npcRule),
            _ => throw new UnreachableException($"Unknown removal zone {_config.ZoneShape}."),
        };
        _meshMessages.PrintAndClear();
        RunReport.PrintTooCloseSummary(hits.Count, world.Targets.Length, _config.Target, timer.Elapsed);
        RunReport.PrintInvisibleOthers(_invisibleOthers, _config.DetailedLog);
        PrintNpcHandling(npcRule, indexes);

        var selection = SplitByProtection(hits, protection);
        RunReport.PrintKept(world, selection.Kept);
        return selection;
    }

    private NpcClashRule CreateNpcClashRule(
        World world,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        Replacements replacements) =>
        NpcClashRule.Create(
            _config.NpcHandling,
            () => NpcStuckSearch.Create(world.Targets, visibility, indexes, replacements, _npcBodies, _shapes, _meshCache, _parallelOptions));

    private void PrintNpcHandling(NpcClashRule npcRule, IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes)
    {
        if (npcRule.StuckSearch is { } stuckSearch)
        {
            RunReport.PrintNpcStuckSummary(stuckSearch.GetSummary(), _npcBodies.GetStats(), _bodyMeasurer.GetMeasurements().Count, _config.DetailedLog);
        }
        else if (npcRule.Handling == NpcHandling.Ignore) RunReport.PrintIgnoredNpcs(CountPlacedNpcs(indexes));
    }

    private static int CountPlacedNpcs(IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes) =>
        indexes.Values.Sum(index => Enumerable.Range(0, index.Count).Count(i => index[i].IsPlacedNpc));

    private List<TooCloseHit> FindShapeZoneHits(
        World world,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        Replacements replacements,
        NpcClashRule npcRule)
    {
        var indexTimer = Stopwatch.StartNew();
        var search = ShapeZoneSearch.Create(
            world.Targets, visibility, indexes, replacements, _shapes, _meshCache, _config.SizeMultiplier, npcRule);
        var indexTime = indexTimer.Elapsed;
        var searchTimer = Stopwatch.StartNew();
        var hits = search.FindTooCloseTargets(visibility, _parallelOptions);
        RunReport.PrintShapeZoneStats(
            search.Stats, search.LargeOtherObjects, _meshCache.GetStats(), _shapes.GetStats().ModelsEffectOnly, indexTime, searchTimer.Elapsed);
        return hits;
    }

    private static TooCloseSelection SplitByProtection(IEnumerable<TooCloseHit> hits, Protection protection)
    {
        var selection = new TooCloseSelection([], []);
        foreach (var hit in hits)
        {
            if (protection.TryGetKeepReason(hit.TargetIndex, out var reason))
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

    /// <param name="seeds">The too-close removals and their linked groups.</param>
    /// <returns>The follow-up removals, with the linked groups of each.</returns>
    private FollowUpRemovals SelectFollowUpRemovals(
        GameSnapshot snapshot,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyList<Removal> seeds,
        TooCloseSelection tooClose,
        SupporterIndex supporters,
        Protection protection)
    {
        if (seeds.Count == 0) return FollowUpRemovals.None;
        return _config.FollowUpMode switch
        {
            FollowUpRemovalMode.Nothing => FollowUpRemovals.None,
            FollowUpRemovalMode.EverythingTouching => SelectTouchingRemovals(snapshot.World, visibility, seeds, tooClose, protection),
            FollowUpRemovalMode.ObjectsSupportedByIt => SelectUnanchoredRemovals(snapshot, visibility, seeds, tooClose, supporters, protection),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {_config.FollowUpMode}."),
        };
    }

    private FollowUpRemovals SelectTouchingRemovals(
        World world,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyList<Removal> seeds,
        TooCloseSelection tooClose,
        Protection protection)
    {
        var clusters = TouchClusterFinder.Find(
            world.Targets,
            visibility,
            ToTargetIndexList(seeds),
            tooClose.KeptIndices,
            _shapes,
            _meshCache,
            protection,
            _config.TouchDistance,
            _parallelOptions,
            collectDiagnostics: _config.WritesDiagnostics);
        RunReport.PrintKept(world, clusters.Kept);
        _meshMessages.PrintAndClear();
        RunReport.PrintTouchStats(clusters);
        WriteTouchDiagnostics(world, seeds, clusters);
        return new FollowUpRemovals(clusters.Removals, clusters.Kept);
    }

    private void WriteTouchDiagnostics(World world, IReadOnlyList<Removal> seeds, TouchClusters clusters)
    {
        if (clusters.Diagnostics is not { } diagnostics) return;

        AccessDiagnosticsFolder($"writing {TouchDiagnosticsWriter.EdgesFileName} / {TouchDiagnosticsWriter.ComponentsFileName}", () =>
        {
            var timer = Stopwatch.StartNew();
            var written = TouchDiagnosticsWriter.Write(_config.DiagnosticsFolder, world, _shapes, _config.TouchDistance, seeds, clusters, diagnostics);
            Console.WriteLine($"Touch diagnostics: wrote {written.EdgeCount:N0} edges, {written.ComponentCount:N0} components in {timer.Elapsed.TotalSeconds:F1}s "
                + $"to {written.EdgesPath} / {written.ComponentsPath}.");
        });
    }

    private FollowUpRemovals SelectUnanchoredRemovals(
        GameSnapshot snapshot,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyList<Removal> seeds,
        TooCloseSelection tooClose,
        SupporterIndex supporters,
        Protection protection)
    {
        var world = snapshot.World;
        var anchoring = AnchoringRemover.Run(
            world.Targets,
            visibility,
            ToTargetIndexList(seeds),
            tooClose.KeptIndices,
            supporters,
            snapshot.Terrain,
            _shapes,
            _meshCache,
            protection,
            _config.TouchDistance,
            _config.AnchoringThreshold,
            _parallelOptions);
        RunReport.PrintKept(world, anchoring.Kept);
        _meshMessages.PrintAndClear();
        RunReport.PrintAnchoringStats(anchoring);
        WriteAnchoringDiagnostics(world, supporters, anchoring);
        WriteMeshOrigins(world.Targets);
        return new FollowUpRemovals(anchoring.Removals, anchoring.Kept);
    }

    private void WriteAnchoringDiagnostics(World world, SupporterIndex supporters, AnchoringResult anchoring)
    {
        if (!_config.WritesDiagnostics) return;

        AccessDiagnosticsFolder($"writing {AnchoringDiagnosticsWriter.FileName}", () =>
        {
            var path = AnchoringDiagnosticsWriter.Write(
                _config.DiagnosticsFolder, world, _shapes, supporters, anchoring.Evaluations, _config.AnchoringThreshold);
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
        World world,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        Replacements replacements,
        IReadOnlyList<Removal> earlierRemovals,
        Protection protection)
    {
        if (!_config.Leftovers.Enabled) return LeftoverResult.None;

        var timer = Stopwatch.StartNew();
        var hosts = Hosts.Find(world.Targets, visibility, indexes, _containment, replacements, _parallelOptions);
        var selector = new LeftoverInvisibleObjectSelector(
            world.Targets,
            visibility,
            _shapes,
            hosts,
            new InvisibleObjectReach(_state.LinkCache, _shapes),
            protection,
            _config.Leftovers);
        var leftovers = selector.SelectRemovals(ToTargetIndices(earlierRemovals), _parallelOptions);
        RunReport.PrintKept(world, leftovers.Kept);
        _meshMessages.PrintAndClear();
        if (_config.DetailedLog) RunReport.PrintLeftoverDecisions(world, leftovers.Evaluations);
        RunReport.PrintLeftoverStats(leftovers, timer.Elapsed);
        return leftovers;
    }

    private static HashSet<int> ToTargetIndices(IEnumerable<Removal> removals) =>
        removals.Select(removal => removal.TargetIndex).ToHashSet();

    private static List<int> ToTargetIndexList(IEnumerable<Removal> removals) =>
        removals.Select(removal => removal.TargetIndex).ToList();

    /// <param name="removals">Every removal of the run.</param>
    private RelocationResult RelocateKeptMarkers(
        GameSnapshot snapshot,
        IReadOnlyList<ObjectVisibility> visibility,
        SupporterIndex supporters,
        IReadOnlyList<Removal> removals,
        LeftoverResult leftovers)
    {
        if (!_config.Leftovers.MovesKeptMarkers) return RelocationResult.None;

        var relocator = CreateRelocator(snapshot, CreateObstacles(snapshot.World, visibility, supporters, removals));
        var removed = ToTargetIndices(removals);
        var keptEvaluations = leftovers.Evaluations.Where(evaluation => !removed.Contains(evaluation.TargetIndex)).ToList();
        var relocations = relocator.Relocate(keptEvaluations, _parallelOptions);
        _meshMessages.PrintAndClear();
        RunReport.PrintRelocations(snapshot.World, relocations);
        return relocations;
    }

    private VisibleObstacles CreateObstacles(
        World world,
        IReadOnlyList<ObjectVisibility> visibility,
        SupporterIndex supporters,
        IReadOnlyList<Removal> removals)
    {
        var remainingVisible = ObjectVisibility.VisibleIndices(visibility, except: ToTargetIndices(removals));
        return new VisibleObstacles(supporters, VisibleTargetIndex.Build(world.Targets, remainingVisible, _shapes), _containment, _shapes);
    }

    private static KeptObjectRelocator CreateRelocator(GameSnapshot snapshot, VisibleObstacles obstacles) => new(
        snapshot.World.Targets,
        index => KeptObjectRelocator.FindHomeCell(snapshot.World.Targets[index], snapshot.Handles.LocationOf(new TargetId(index))),
        [
            new NavmeshSpotSearch(new NavmeshIndex(snapshot.Navmeshes), obstacles),
            new TerrainSpotSearch(snapshot.Terrain, obstacles),
        ]);

    private void WriteLeftoverDiagnostics(World world, LeftoverResult leftovers, RelocationResult relocations)
    {
        if (!_config.WritesDiagnostics || !_config.Leftovers.Enabled) return;

        AccessDiagnosticsFolder($"writing {LeftoverDiagnosticsWriter.FileName}", () =>
        {
            var path = LeftoverDiagnosticsWriter.Write(_config.DiagnosticsFolder, world, _shapes, leftovers.Evaluations, relocations);
            Console.WriteLine($"Leftover invisible objects diagnostics: wrote {leftovers.Evaluations.Count:N0} evaluations to {path}.");
        });
    }

    private void AccessDiagnosticsFolder(string description, Action access) =>
        DiagnosticsFiles.Access(_config.DiagnosticsFolder, description, access);

    private void WriteOverrides(GameSnapshot snapshot, IReadOnlyList<Removal> removals, IReadOnlyList<Relocation> moves)
    {
        var timer = Stopwatch.StartNew();
        var handles = snapshot.Handles;
        var overrides = new PlacedOverrideWriter(_state.PatchMod);
        var remover = new ObjectRemover(overrides);
        var enableParentsReplaced = 0;
        foreach (var removal in removals)
        {
            var id = new TargetId(removal.TargetIndex);
            if (remover.Disable(handles.RecordOf(id), handles.LocationOf(id))) enableParentsReplaced++;
        }

        var mover = new ObjectMover(overrides);
        foreach (var move in moves)
        {
            var id = new TargetId(move.Evaluation.TargetIndex);
            mover.MoveTo(handles.RecordOf(id), handles.LocationOf(id), move.To);
        }
        RunReport.PrintWriteSummary(removals.Count, moves.Count, enableParentsReplaced, timer.Elapsed);
    }

    private void PrintFinalReport(
        World world,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        IReadOnlyList<Removal> removals,
        IReadOnlyList<KeptTarget> kept)
    {
        if (_config.DetailedLog) RunReport.PrintRemovals(world, _shapes, removals);
        RunReport.PrintBoundsStats(_shapes.GetStats());
        if (_config.DetailedLog) RunReport.PrintSpaceSummary(world, indexes, removals);
        RunReport.PrintRemovalSummary(removals);
        RunReport.PrintKeptSummary(kept);
    }

    private void ReportManualPatchHints(
        World world,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyList<Removal> removals,
        IReadOnlyList<KeptTarget> kept,
        ManualPatchHints hintCollector)
    {
        RunReport.PrintRemovedMarkersByType(removals, visibility);
        var hints = hintCollector.Collect(removals, kept);
        RunReport.PrintManualPatchHints(world, hints);
        if (!_config.WritesDiagnostics) return;

        AccessDiagnosticsFolder($"writing {ManualPatchHintsWriter.FileName}", () =>
        {
            var path = ManualPatchHintsWriter.Write(_config.DiagnosticsFolder, world, hints);
            Console.WriteLine($"Manual patch hints: wrote {hints.Count:N0} rows to {path}.");
        });
    }
}
