using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>
/// Runs the steps of one patch run in order and prints each phase's log section right after it.
/// All decisions are made before the patch is written; the report files' evidence is computed after.
/// </summary>
internal static class Composition
{
    public static RunResult Run(RunOptions options, ModFacts mods, IGameReader game, Assets assets, IRunLog log)
    {
        var context = assets.ReportContext(options.DetailedLog);
        var standing = Standing.Decide(mods, options.Standing);
        log.Print(LogSections.Config(options, mods, standing));
        var snapshot = log.Timed(
            () => game.Read(mods, standing, options.Read),
            (read, elapsed) => LogSections.Scan(read.World, options.TargetKey, elapsed, context));
        if (snapshot.World.Targets.IsEmpty) return new RunResult.Stopped(new StopReason(StopKind.NoTargetObjects, null));

        log.Print(LogSections.Threads(options.Execution, context));
        var decided = Decide(options, snapshot, assets, log, context);
        var removals = Decisions.Removals(decided.Final, decided.World, decided.Leftovers).ToImmutableArray();
        var written = log.Timed(
            () => game.Write(PlanPatch(decided, removals), snapshot.Handles),
            (summary, elapsed) => LogSections.Write(summary, elapsed, context));
        log.Print(LogSections.BoundsIndexTimes(assets.Clock.Times(), context));

        var explanations = Explain(options, decided, assets);
        log.Print(LogSections.ExplanationProblems(assets.TakeProblems(), context));
        return new RunResult.Done(ToOutcome(decided, removals, written, assets), explanations);
    }

    /// <summary>Every decision of the run, from the setup through the relocations.</summary>
    private static Decided Decide(RunOptions options, GameSnapshot snapshot, Assets assets, IRunLog log, ReportContext context)
    {
        var world = snapshot.World;
        var services = assets.Services;
        var scene = log.Timed(
            () => Scene.Create(world, services.Shapes, services.Triangles, services.Bodies, options.Execution, assets.Clock),
            (_, elapsed) => LogSections.Index(world, elapsed, context));
        var order = WorkOrder.Of(world.Targets);
        var looks = log.Timed(
            () => Looks.OfTargets(world.Targets, services.Shapes, options.Execution),
            (_, elapsed) => LogSections.WarmUp(Looks.BasesOf(world.Targets).Length, elapsed, assets.TakeProblems(), context));
        log.Print(LogSections.TargetVisibility(looks, world.Counts.TargetsHiddenOrWithoutPlacement, context));
        var replacements = log.Timed(
            () => ReplacementMatcher.Find(world.Targets, scene.RivalPositions(), world.Rivals.Length, services.Shapes, order, options.Execution),
            (found, elapsed) => LogSections.Replacements(world, found, assets.TakeProblems(), elapsed, context));
        var protection = Protection.Build(world.Targets, world.Links, world.References);
        log.Print(LogSections.Groups(protection.Groups, world.Counts.TargetPluginLinks, context));
        var rivals = scene.ActiveRivals(replacements, options.Clash.Npcs);

        var scope = new PhaseScope(options, snapshot, looks, scene, assets, log, context);
        var clashes = FindClashes(scope, rivals, replacements, protection, order);
        var followUp = RunFollowUp(scope, protection, clashes.Ledger);
        var leftovers = DecideLeftovers(scope, rivals, order, followUp.Result.Ledger);
        var relocations = RelocateKeptMarkers(scope, leftovers);
        return new Decided(
            world, looks, clashes.Census, replacements, protection, leftovers.Hosts, clashes.Result, followUp.Result, followUp.Components,
            leftovers.Result, relocations, leftovers.Final);
    }

    private static ClashPhase FindClashes(
        PhaseScope scope, IActiveRivals rivals, Replacements replacements, Protection protection, WorkOrder order)
    {
        var (options, world, looks, assets) = (scope.Options, scope.World, scope.Looks, scope.Assets);
        var services = assets.Services;
        var npcs = options.Clash.Npcs == NpcHandling.OnlyWhenStuckInObject ? scope.Scene.Npcs(replacements) : null;
        var input = new ClashInput(
            world.Targets, looks, order, rivals, npcs, services.Shapes, services.Triangles, options.Execution, assets.Clock);
        var phase = scope.Log.Timed(
            () => DecideTooClose(input, options.Clash, protection, scope.Scene, assets),
            (decided, elapsed) => LogSections.TooClose(TooCloseReportOf(options, world, decided, assets, elapsed), scope.Context));
        scope.Log.Print(LogSections.Linked(phase.Ledger, [phase.Ledger.Rounds[^1]], "too-close", scope.Context));
        return phase;
    }

    /// <remarks>The phase's mesh problems are taken before the census, whose rival reads belong to the next phase.</remarks>
    private static ClashPhase DecideTooClose(ClashInput input, ClashOptions options, Protection protection, Scene scene, Assets assets)
    {
        var result = Clashes.Find(input, options);
        var ledger = Ledger.Start(protection, input.Targets.Length).Apply(RoundKind.TooClose, result.Proposals);
        var problems = assets.TakeProblems();
        return new ClashPhase(result, ledger, problems, scene.Census(input.Looks));
    }

    private static TooCloseReport TooCloseReportOf(RunOptions options, World world, ClashPhase phase, Assets assets, TimeSpan elapsed)
    {
        var times = assets.Clock.Times();
        return new TooCloseReport(
            world,
            options.TargetKey,
            options.Clash,
            phase.Result,
            phase.Census,
            [.. Decisions.KeptIn(phase.Ledger, phase.Ledger.Rounds[^1])],
            phase.Problems,
            new ClashPerf(assets.Perf.Triangles(), assets.Perf.Bodies(), assets.Services.Shapes.GetStats().ModelsEffectOnly),
            new TooCloseTimes(elapsed, times.Total(TimedPhase.ShapeZoneIndexBuild), times.Total(TimedPhase.ShapeZoneSearch)));
    }

    /// <param name="afterClashes">The ledger whose last round is the too-close round.</param>
    private static FollowUpPhase RunFollowUp(PhaseScope scope, Protection protection, Ledger afterClashes)
    {
        var (options, world, looks, scene, assets) = (scope.Options, scope.World, scope.Looks, scope.Scene, scope.Assets);
        var services = assets.Services;
        var input = new FollowUpInput(
            world.Targets,
            looks,
            protection,
            CollectSolidsIfSupportRoundsRun(options, scene, afterClashes),
            services.Shapes,
            services.Triangles,
            scope.Snapshot.Terrain,
            options.Execution,
            assets.Clock);
        var followUp = FollowUp.Run(afterClashes, input, options.FollowUp);
        var components = NeedsTouchComponents(options, followUp)
            ? FollowUp.Components(followUp, world.Targets.Length, options.Execution)
            : null;
        if (!followUp.Rounds.IsEmpty) scope.Log.Print(FollowUpSection(world, followUp, components, assets, scope.Context));
        scope.Log.Print(LogSections.Linked(followUp.Ledger, followUp.Rounds, "follow-up", scope.Context));
        return new FollowUpPhase(followUp, components);
    }

    /// <remarks>Only asked for when needed, because the first ask builds the supporters' bounds index.</remarks>
    private static ISolids? CollectSolidsIfSupportRoundsRun(RunOptions options, Scene scene, Ledger afterClashes) =>
        options.FollowUp.Mode == FollowUpRemovalMode.ObjectsSupportedByIt && !afterClashes.RemovedIn(afterClashes.Rounds[^1]).IsEmpty
            ? scene.Solids()
            : null;

    /// <summary>Finding the components tests the touching pairs again, so only the detailed log and the report files ask for them.</summary>
    private static bool NeedsTouchComponents(RunOptions options, FollowUpResult followUp) =>
        followUp.Mode == FollowUpRemovalMode.EverythingTouching
        && followUp.HadSeeds
        && (options.DetailedLog || options.Reports.WriteFiles);

    private static LogSection FollowUpSection(
        World world, FollowUpResult followUp, TouchComponentSet? components, Assets assets, ReportContext context) =>
        followUp.Mode == FollowUpRemovalMode.EverythingTouching
            ? LogSections.Touch(world, followUp, components, assets.TakeProblems(), assets.Clock.Times(), assets.Perf.Triangles(), context)
            : LogSections.Anchoring(world, followUp, assets.TakeProblems(), assets.Clock.Times(), assets.Perf.Triangles(), context);

    /// <param name="afterFollowUp">The ledger after the follow-up rounds.</param>
    private static LeftoverPhase DecideLeftovers(PhaseScope scope, IActiveRivals rivals, WorkOrder order, Ledger afterFollowUp)
    {
        var (options, world, assets, context) = (scope.Options, scope.World, scope.Assets, scope.Context);
        var phase = options.Leftovers is { } leftoverOptions
            ? scope.Log.Timed(
                () => EvaluateLeftovers(scope, rivals, order, afterFollowUp, leftoverOptions),
                (evaluated, elapsed) => LogSections.Leftovers(
                    world, evaluated.Result, Decisions.KeptIn(evaluated.Final, evaluated.Final.Rounds[^1]), assets.TakeProblems(), elapsed, context))
            : new LeftoverPhase(Hosts.NotComputed, LeftoverResult.None, afterFollowUp.Apply(RoundKind.Leftover, []));
        scope.Log.Print(LogSections.Linked(phase.Final, [phase.Final.Rounds[^1]], "leftover invisible object", context));
        return phase;
    }

    private static LeftoverPhase EvaluateLeftovers(
        PhaseScope scope, IActiveRivals rivals, WorkOrder order, Ledger afterFollowUp, LeftoverOptions options)
    {
        var (world, looks, execution) = (scope.World, scope.Looks, scope.Options.Execution);
        var hosts = Hosts.Find(world.Targets, looks, rivals, order, execution);
        var evaluated = Leftovers.Evaluate(
            new LeftoverInput(world.Targets, looks, hosts, afterFollowUp, scope.Scene.VisibleTargets(looks), scope.Assets.Services.Bases, order, execution),
            options);
        var final = afterFollowUp.Apply(RoundKind.Leftover, [.. evaluated.Proposals]);
        return new LeftoverPhase(hosts, evaluated.WithVerdicts(final), final);
    }

    private static RelocationResult RelocateKeptMarkers(PhaseScope scope, LeftoverPhase leftovers)
    {
        var (options, snapshot, world, scene) = (scope.Options, scope.Snapshot, scope.World, scope.Scene);
        if (options.Relocation is not { } relocationOptions) return RelocationResult.None;

        var input = new RelocationInput(
            world.Targets,
            Relocations.Candidates(leftovers.Result, leftovers.Final),
            new Obstacles(scene.Solids(), scene.VisibleTargets(scope.Looks), leftovers.Final),
            snapshot.Handles,
            snapshot.Navmeshes,
            snapshot.Terrain,
            scope.Assets.Services.Shapes,
            options.Execution);
        var relocations = Relocations.Plan(input, relocationOptions);
        scope.Log.Print(LogSections.Relocations(world, relocations, relocationOptions, scope.Assets.TakeProblems(), scope.Context));
        return relocations;
    }

    /// <summary>The removals in the order the removal list reports them, and the moves.</summary>
    private static PatchPlan PlanPatch(Decided decided, ImmutableArray<Removal> removals) =>
        new(
            [.. removals.Select(removal => new TargetId(removal.TargetIndex))],
            [.. decided.Relocation.Moved]);

    /// <summary>The evidence only the report files show; none when no files are written.</summary>
    private static Explanations Explain(RunOptions options, Decided decided, Assets assets)
    {
        if (!options.Reports.WriteFiles) return Explanations.None;
        var services = assets.Services;
        return Explanations.Compute(new ExplainInput(
            decided.FollowUp, decided.Components, decided.World.Targets, services.Shapes, services.Triangles, services.Bases, options.Execution));
    }

    /// <remarks>Taken after the explanations, so their mesh reads and problems are counted.</remarks>
    private static Outcome ToOutcome(Decided decided, ImmutableArray<Removal> removals, WriteSummary written, Assets assets)
    {
        var problems = assets.AllProblems();
        return new Outcome(decided, removals, written, problems.Assets, problems.Archive, assets.Services.Shapes.GetStats());
    }

    /// <summary>What every phase function reads besides its own inputs.</summary>
    private sealed record PhaseScope(
        RunOptions Options, GameSnapshot Snapshot, TargetLooks Looks, Scene Scene, Assets Assets, IRunLog Log, ReportContext Context)
    {
        public World World => Snapshot.World;
    }

    /// <param name="Problems">The mesh problems the phase met.</param>
    private sealed record ClashPhase(ClashResult Result, Ledger Ledger, PhaseProblems Problems, RivalCensus Census);

    /// <param name="Components">Null unless the touch rounds ran and the detailed log or the report files need them.</param>
    private sealed record FollowUpPhase(FollowUpResult Result, TouchComponentSet? Components);

    /// <param name="Final">The ledger with every round.</param>
    private sealed record LeftoverPhase(Hosts Hosts, LeftoverResult Result, Ledger Final);
}
