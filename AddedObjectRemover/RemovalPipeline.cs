using System.Collections.Immutable;
using System.Diagnostics;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover;

/// <summary>
/// Runs the phases of one patch run in order. All computation finishes before overrides are
/// written, single-threaded, into the patch.
/// </summary>
internal sealed class RemovalPipeline
{
    private readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> _state;
    private readonly RunConfig _config;
    private readonly ModFacts _mods;
    private readonly ModStanding _standing;
    private readonly ParallelOptions _parallelOptions;
    private readonly AssetProblemLog _problems = new();
    private ProblemMark _problemsPrintedUpTo;
    private readonly BaseFactsReader _bases;
    private readonly ShapeCatalog _shapes;
    private readonly TriangleStore _meshCache;
    private readonly SkinnedBodyMeasurer _bodyMeasurer;
    private readonly NpcBodyCache _npcBodies;
    private readonly IPerfProbe _perf;
    private readonly PhaseClock _clock = new();

    public RemovalPipeline(
        IPatcherState<ISkyrimMod, ISkyrimModGetter> state, RunConfig config, ModFacts mods, ModStanding standing, int workers)
    {
        _state = state;
        _config = config;
        _mods = mods;
        _standing = standing;
        _parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = workers };
        var meshFiles = new MeshFileSource(
            state.DataFolderPath.Path,
            state.GameRelease,
            state.LoadOrder.ListedOrder.Select(listing => listing.ModKey).ToList(),
            _problems);
        _bases = new BaseFactsReader(state.LinkCache);
        _shapes = new ShapeCatalog(_bases, meshFiles, _problems);
        _meshCache = new TriangleStore(_shapes.ReadGeometry);
        _bodyMeasurer = new SkinnedBodyMeasurer(_shapes.ReadGeometry);
        _npcBodies = new NpcBodyCache(new NpcBodyResolver(state.LinkCache, _shapes, _bodyMeasurer));
        _perf = new PerfProbe(_meshCache, _npcBodies, _bodyMeasurer);
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

        var scene = CreateScene(world);
        var order = WorkOrder.Of(world.Targets);
        WarmUpTargetBounds(world.Targets);
        var visibility = ClassifyTargetVisibility(world.Targets);
        RunReport.PrintTargetVisibility(visibility, world.Counts.TargetsHiddenOrWithoutPlacement);
        var replacements = FindReplacements(world, scene.RivalPositions(), order);

        var protection = Protection.Build(world.Targets, world.Links, world.References);
        RunReport.PrintLinkedGroups(protection.Groups, world.Counts.TargetPluginLinks);
        var rivals = scene.ActiveRivals(replacements, _config.NpcHandling);
        var ledger = Ledger.Start(protection, world.Targets.Length);
        var tooClose = DecideTooClose(world, visibility, scene, rivals, replacements, order, ledger);
        PrintLinkedRemovals(tooClose, RoundsAddedTo(ledger, tooClose), "too-close");
        var followUp = DecideFollowUp(snapshot, visibility, scene, protection, tooClose);
        PrintLinkedRemovals(followUp, RoundsAddedTo(tooClose, followUp), "follow-up");
        var (final, leftovers) = DecideLeftovers(world, visibility, scene, rivals, order, followUp);
        PrintLinkedRemovals(final, RoundsAddedTo(followUp, final), "leftover invisible object");

        var report = new LedgerReport(final, world, leftovers);
        var removals = report.Removals();
        var relocations = RelocateKeptMarkers(snapshot, visibility, scene, final, leftovers);
        WriteLeftoverDiagnostics(world, leftovers, relocations);
        WriteOverrides(snapshot, removals, relocations.Moved);
        RunReport.PrintBoundsIndexTimes(_clock);

        var kept = report.Kept();
        PrintFinalReport(world, removals, kept);
        ReportManualPatchHints(world, visibility, removals, kept, new ManualPatchHints(world.Targets, visibility, protection));
        Console.WriteLine($"Done in {totalTimer.Elapsed.TotalSeconds:F1}s.");
    }

    /// <summary>The rounds <paramref name="after"/> has beyond <paramref name="before"/>, an older view of the same run.</summary>
    private static ImmutableArray<Round> RoundsAddedTo(Ledger before, Ledger after) => [.. after.Rounds.Skip(before.Rounds.Length)];

    /// <summary>How many objects the step's rounds removed as linked to their own removals.</summary>
    private static void PrintLinkedRemovals(Ledger ledger, IEnumerable<Round> rounds, string step) =>
        RunReport.PrintLinkedRemovals(rounds.Sum(round => LedgerReport.CountLinkedIn(ledger, round)), step);

    private static void PrintKept(World world, Ledger ledger, IEnumerable<Round> rounds) =>
        RunReport.PrintKept(world, rounds.SelectMany(round => LedgerReport.KeptIn(ledger, round)));

    private static HashSet<int> CollectRemoved(Ledger ledger) =>
        ledger.All().Where(entry => entry.Verdict is Verdict.Removed).Select(entry => entry.Target.Index).ToHashSet();

    private GameSnapshot ScanLoadOrder()
    {
        var timer = Stopwatch.StartNew();
        var snapshot = PlacedRecordScanner.Scan(_state, _config, _mods.Mods, _standing);
        RunReport.PrintOverriddenOthers(snapshot.World, _config.Target);
        RunReport.PrintScanSummary(snapshot.World, _config.Target, timer.Elapsed);
        return snapshot;
    }

    private Scene CreateScene(World world)
    {
        var timer = Stopwatch.StartNew();
        var scene = Scene.Create(world, _shapes, _meshCache, _npcBodies, _parallelOptions, _clock);
        RunReport.PrintIndexSummary(world, timer.Elapsed);
        return scene;
    }

    private void PrintProblemsSinceLastPhase()
    {
        if (NifGeometryReader.LoaderWarmUpProblem is { } warmUp) _problems.Add(warmUp);
        if (NifShapes.StripFieldsProblem is { } stripFields) _problems.Add(stripFields);
        RunReport.PrintArchiveProblems(_problems.ArchiveProblemsSince(_problemsPrintedUpTo));
        if (_config.DetailedLog) RunReport.PrintAssetProblems(_problems.Since(_problemsPrintedUpTo));
        _problemsPrintedUpTo = _problems.Mark();
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
        PrintProblemsSinceLastPhase();
        RunReport.PrintWarmUpSummary(targetBases.Count, timer.Elapsed);
    }

    /// <returns>Parallel to <paramref name="targets"/>.</returns>
    private ObjectVisibility[] ClassifyTargetVisibility(IReadOnlyList<TargetObject> targets) =>
        ParallelMap.Run(_parallelOptions, targets.Count, index =>
        {
            var target = targets[index];
            return _shapes.GetVisibility(target.Base, target.IsPrimitive, target.HasMapMarker);
        });

    private Replacements FindReplacements(World world, IRivalPositions rivals, WorkOrder order)
    {
        var timer = Stopwatch.StartNew();
        var replacements = ReplacementMatcher.Find(world.Targets, rivals, world.Rivals.Length, _shapes, order, _parallelOptions);
        if (_config.DetailedLog) RunReport.PrintReplacementLog(world, replacements);
        PrintProblemsSinceLastPhase();
        RunReport.PrintReplacementSummary(replacements.Count, timer.Elapsed);
        return replacements;
    }

    /// <returns>The ledger with the too-close round.</returns>
    private Ledger DecideTooClose(
        World world,
        IReadOnlyList<ObjectVisibility> visibility,
        Scene scene,
        IActiveRivals rivals,
        Replacements replacements,
        WorkOrder order,
        Ledger ledger)
    {
        var timer = Stopwatch.StartNew();
        var npcRule = CreateNpcClashRule(world, visibility, scene, replacements);
        var result = _config.ZoneShape switch
        {
            ZoneShape.BoundingBox => TooCloseSearch.FindTooCloseTargets(
                world.Targets, visibility, rivals, _shapes, _config.SizeMultiplier, npcRule, order, _parallelOptions),
            ZoneShape.ObjectShape => FindShapeZoneHits(world, visibility, rivals, npcRule, order),
            _ => throw new UnreachableException($"Unknown removal zone {_config.ZoneShape}."),
        };
        var hits = result.Hits;
        PrintProblemsSinceLastPhase();
        RunReport.PrintTooCloseSummary(hits.Count, world.Targets.Length, _config.Target, timer.Elapsed);
        var census = scene.Census(visibility);
        RunReport.PrintInvisibleOthers(census, _config.DetailedLog);
        PrintNpcHandling(npcRule, census, result.Work.Npcs);

        var next = ledger.Apply(
            RoundKind.TooClose,
            [.. hits.Select(hit => new Proposal(new TargetId(hit.TargetIndex), new Cause.TooClose(hit.TooCloseTo.Id)))]);
        PrintKept(world, next, RoundsAddedTo(ledger, next));
        return next;
    }

    private NpcClashRule CreateNpcClashRule(World world, IReadOnlyList<ObjectVisibility> visibility, Scene scene, Replacements replacements) =>
        NpcClashRule.Create(
            _config.NpcHandling,
            () => NpcStuckSearch.Create(world.Targets, visibility, scene.Npcs(replacements), _shapes, _meshCache));

    private void PrintNpcHandling(NpcClashRule npcRule, RivalCensus census, NpcWork work)
    {
        if (npcRule.StuckSearch is { } stuckSearch)
        {
            RunReport.PrintNpcStuckSummary(stuckSearch.GetSummary(work), _perf.Bodies(), _config.DetailedLog);
        }
        else if (npcRule.Handling == NpcHandling.Ignore) RunReport.PrintIgnoredNpcs(census.PlacedNpcs);
    }

    private ClashSearchResult FindShapeZoneHits(
        World world,
        IReadOnlyList<ObjectVisibility> visibility,
        IActiveRivals rivals,
        NpcClashRule npcRule,
        WorkOrder order)
    {
        var indexTimer = Stopwatch.StartNew();
        var search = ShapeZoneSearch.Create(world.Targets, visibility, rivals, _shapes, _meshCache, _config.SizeMultiplier, npcRule);
        var indexTime = indexTimer.Elapsed;
        var searchTimer = Stopwatch.StartNew();
        var result = search.FindTooCloseTargets(visibility, order, _parallelOptions);
        RunReport.PrintShapeZoneStats(
            result.Work.Zone, search.LargeOtherObjects, _perf.Triangles(), _shapes.GetStats().ModelsEffectOnly, indexTime, searchTimer.Elapsed);
        return result;
    }

    /// <summary>The follow-up rounds, seeded with the too-close round's removals; none without seeds or with follow-up removal off.</summary>
    /// <returns>The ledger with the follow-up rounds.</returns>
    private Ledger DecideFollowUp(
        GameSnapshot snapshot,
        IReadOnlyList<ObjectVisibility> visibility,
        Scene scene,
        Protection protection,
        Ledger ledger)
    {
        var tooCloseRound = ledger.Rounds[^1];
        var seeds = ledger.RemovedIn(tooCloseRound).Select(target => target.Index).ToList();
        var keptTooClose = ledger.HeldIn(tooCloseRound).Select(target => target.Index).ToList();
        if (seeds.Count == 0) return ledger;
        return _config.FollowUpMode switch
        {
            FollowUpRemovalMode.Nothing => ledger,
            FollowUpRemovalMode.EverythingTouching => DecideTouching(snapshot.World, visibility, seeds, keptTooClose, protection, ledger),
            FollowUpRemovalMode.ObjectsSupportedByIt => DecideUnanchored(snapshot, visibility, seeds, scene.Solids(), protection, ledger),
            _ => throw new UnreachableException($"Unknown follow-up removal mode {_config.FollowUpMode}."),
        };
    }

    private Ledger DecideTouching(
        World world,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyList<int> seeds,
        IReadOnlyList<int> keptTooClose,
        Protection protection,
        Ledger ledger)
    {
        var search = _clock.Time(TimedPhase.TouchSetup, () => TouchSearch.Create(
            world.Targets,
            visibility,
            protection.Groups.CollectReachableSpaces(world.Targets, seeds),
            excluded: keptTooClose,
            _shapes,
            _meshCache,
            _config.TouchDistance,
            _parallelOptions));
        var rule = new TouchRule(search, _parallelOptions, _clock);
        var (next, evidence) = Cascade.Run(ledger, rule);
                var rounds = RoundsAddedTo(ledger, next);
        var report = new LedgerReport(next, world, LeftoverResult.None);
        var clusters = TouchComponents.Find(
            next,
            report,
            world.Targets.Length,
            ledger.Rounds[^1],
            rounds,
            evidence,
            search,
            _parallelOptions,
            _clock,
            collectDiagnostics: _config.WritesDiagnostics);
        PrintKept(world, next, rounds);
        PrintProblemsSinceLastPhase();
        RunReport.PrintTouchStats(
            clusters,
            new TouchTimes(
                _clock.Total(TimedPhase.TouchSetup),
                _clock.Total(TimedPhase.TouchBroadPhase),
                _clock.Total(TimedPhase.TouchNarrowPhase),
                _clock.Total(TimedPhase.TouchDiagnosticsEdges)),
            _perf.Triangles());
        WriteTouchDiagnostics(world, [.. report.RemovalsIn(ledger.Rounds[^1])], clusters);
        return next;
    }

    private SupportRule CreateSupportRule(
        GameSnapshot snapshot,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyList<int> seeds,
        ISolids solids,
        Protection protection,
        IPhaseTimer timer)
    {
        var targets = snapshot.World.Targets;
        var search = TouchSearch.Create(
            targets, visibility, protection.Groups.CollectReachableSpaces(targets, seeds), excluded: [], _shapes, _meshCache, _config.TouchDistance, _parallelOptions);
        var supporterFinder = new AnchoringSupporterFinder(targets, search, solids, _shapes, _config.TouchDistance);
        var contactFinder = new AnchoringContactFinder(targets, search.MeshPaths, supporterFinder, snapshot.Terrain, search.Cache, _config.TouchDistance);
        return new SupportRule(search, contactFinder, _config.AnchoringThreshold, targets.Length, _parallelOptions, timer);
    }

    private void WriteTouchDiagnostics(World world, IReadOnlyList<Removal> seeds, TouchClusters clusters)
    {
        if (clusters.Diagnostics is not { } diagnostics) return;

        AccessDiagnosticsFolder($"writing {TouchDiagnosticsWriter.EdgesFileName} / {TouchDiagnosticsWriter.ComponentsFileName}", () =>
        {
            var timer = Stopwatch.StartNew();
            var written = TouchDiagnosticsWriter.Write(_config.DiagnosticsFolder, world, _shapes, _bases, _config.TouchDistance, seeds, clusters, diagnostics);
            Console.WriteLine($"Touch diagnostics: wrote {written.EdgeCount:N0} edges, {written.ComponentCount:N0} components in {timer.Elapsed.TotalSeconds:F1}s "
                + $"to {written.EdgesPath} / {written.ComponentsPath}.");
        });
    }

    /// <remarks>Objects held in the too-close round are decided, so they are never candidates, and they keep supporting others.</remarks>
    private Ledger DecideUnanchored(
        GameSnapshot snapshot,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyList<int> seeds,
        ISolids solids,
        Protection protection,
        Ledger ledger)
    {
        var world = snapshot.World;
        var rule = _clock.Time(TimedPhase.AnchoringSetup, () => CreateSupportRule(snapshot, visibility, seeds, solids, protection, _clock));
        var (next, evidence) = Cascade.Run(ledger, rule);
                var rounds = RoundsAddedTo(ledger, next);
        var anchoring = AnchoringOutcome.Create(
            next, new LedgerReport(next, world, LeftoverResult.None), rounds, evidence);
        PrintKept(world, next, rounds);
        PrintProblemsSinceLastPhase();
        RunReport.PrintAnchoringStats(
            anchoring,
            new AnchoringTimes(
                _clock.Total(TimedPhase.AnchoringSetup),
                _clock.Total(TimedPhase.AnchoringTouchSearch),
                _clock.Total(TimedPhase.AnchoringContactPoints)),
            _perf.Triangles());
        WriteAnchoringDiagnostics(world, anchoring);
        WriteMeshOrigins(world.Targets);
        return next;
    }

    private void WriteAnchoringDiagnostics(World world, AnchoringResult anchoring)
    {
        if (!_config.WritesDiagnostics) return;

        AccessDiagnosticsFolder($"writing {AnchoringDiagnosticsWriter.FileName}", () =>
        {
            var path = AnchoringDiagnosticsWriter.Write(
                _config.DiagnosticsFolder, world, _shapes, _bases, anchoring.Evaluations, _config.AnchoringThreshold);
            Console.WriteLine($"Anchoring diagnostics: wrote {anchoring.Evaluations.Count:N0} evaluations to {path}.");
        });
    }

    private void WriteMeshOrigins(IReadOnlyList<TargetObject> targets)
    {
        if (!_config.WritesDiagnostics) return;

        var rows = MeshOriginDiagnosticsWriter.CreateRows(targets, _shapes, _bases, _meshCache, _parallelOptions);
        PrintProblemsSinceLastPhase();
        RunReport.PrintMeshOriginSummary(MeshOriginDiagnosticsWriter.Summarize(rows));
        AccessDiagnosticsFolder($"writing {MeshOriginDiagnosticsWriter.FileName}", () =>
        {
            var path = MeshOriginDiagnosticsWriter.Write(_config.DiagnosticsFolder, rows);
            Console.WriteLine($"  Wrote {rows.Count:N0} mesh origins to {path}.");
        });
    }

    /// <returns>The final ledger, with the leftover round, and the leftover evaluations joined with its verdicts.</returns>
    private (Ledger Final, LeftoverResult Leftovers) DecideLeftovers(
        World world,
        IReadOnlyList<ObjectVisibility> visibility,
        Scene scene,
        IActiveRivals rivals,
        WorkOrder order,
        Ledger ledger)
    {
        if (!_config.Leftovers.Enabled) return (ledger.Apply(RoundKind.Leftover, []), LeftoverResult.None);

        var timer = Stopwatch.StartNew();
        var hosts = Hosts.Find(world.Targets, visibility, rivals, order, _parallelOptions);
        var selector = new LeftoverInvisibleObjectSelector(
            world.Targets,
            visibility,
            scene.VisibleTargets(visibility),
            hosts,
            new InvisibleObjectReach(_bases),
            _config.Leftovers, order);
        var evaluated = selector.SelectRemovals(CollectRemoved(ledger), _parallelOptions);
        var final = ledger.Apply(RoundKind.Leftover, evaluated.Proposals);
        var leftovers = evaluated.WithVerdicts(final);
        PrintKept(world, final, RoundsAddedTo(ledger, final));
        PrintProblemsSinceLastPhase();
        if (_config.DetailedLog) RunReport.PrintLeftoverDecisions(world, leftovers.Evaluations);
        RunReport.PrintLeftoverStats(leftovers, timer.Elapsed);
        return (final, leftovers);
    }

    private RelocationResult RelocateKeptMarkers(
        GameSnapshot snapshot,
        IReadOnlyList<ObjectVisibility> visibility,
        Scene scene,
        Ledger final,
        LeftoverResult leftovers)
    {
        if (!_config.Leftovers.MovesKeptMarkers) return RelocationResult.None;

        var removed = CollectRemoved(final);
        var obstacles = new VisibleObstacles(scene.Solids(), scene.VisibleTargets(visibility), removed, _shapes);
        var relocator = CreateRelocator(snapshot, obstacles);
        var keptEvaluations = leftovers.Evaluations.Where(evaluation => !removed.Contains(evaluation.TargetIndex)).ToList();
        var relocations = relocator.Relocate(keptEvaluations, _parallelOptions);
        PrintProblemsSinceLastPhase();
        RunReport.PrintRelocations(snapshot.World, relocations);
        return relocations;
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
            var path = LeftoverDiagnosticsWriter.Write(_config.DiagnosticsFolder, world, _bases, leftovers.Evaluations, relocations);
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

    private void PrintFinalReport(World world, IReadOnlyList<Removal> removals, IReadOnlyList<KeptTarget> kept)
    {
        if (_config.DetailedLog) RunReport.PrintRemovals(world, _bases, removals);
        RunReport.PrintBoundsStats(_shapes.GetStats());
        if (_config.DetailedLog) RunReport.PrintSpaceSummary(world, removals);
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
