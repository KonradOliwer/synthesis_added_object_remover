using System.Collections.Immutable;
using System.Diagnostics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>Console output of a run: configuration, per-phase summaries, verbose details and final statistics.</summary>
internal static class RunReport
{
    private const double BytesPerMegabyte = 1024.0 * 1024.0;

    public static void PrintConfig(RunConfig config, ModFacts mods, ModStanding standing)
    {
        static string Join(IEnumerable<ModKey> keys)
        {
            var names = keys.ToList();
            return names.Count == 0 ? "(none)" : string.Join(", ", names);
        }

        Console.WriteLine($"Target plugin: {config.Target}");
        Console.WriteLine($"Size multiplier: {config.SizeMultiplier}");
        Console.WriteLine($"Removal zone: {config.ZoneShape}");
        Console.WriteLine($"Excluded plugins: {Join(config.ExcludedPlugins)}");
        Console.WriteLine(config.Standing.IgnoreTargetMasters
            ? $"Ignored masters of target: {Join(standing.IgnoredMasters.Select(mods.Mods.KeyOf))}"
            : "Masters of target are not ignored.");
        PrintCompatibilityPatches(standing.Patches, mods.Mods);
        Console.WriteLine($"NPCs and creatures: {config.NpcHandling}");
        Console.WriteLine(DescribeFollowUpRemoval(config));
        Console.WriteLine(DescribeLeftoverRemoval(config));
        Console.WriteLine($"Detailed log: {config.DetailedLog}");
        Console.WriteLine($"Write report files: {config.WritesDiagnostics}");
        Console.WriteLine($"Report folder: {config.DiagnosticsFolder}");
    }

    private static void PrintCompatibilityPatches(PatchReport patches, ModTable mods)
    {
        if (!patches.DetectionOn)
        {
            Console.WriteLine("Compatibility patches: detection off.");
            return;
        }
        Console.WriteLine($"Compatibility patch detection: {patches.PluginsMasteringTarget:N0} plugins master the target.");
        foreach (var skipped in patches.SkippedTooManyMasters)
        {
            Console.WriteLine(
                $"  Plugin {mods.KeyOf(skipped.Patch)} skipped: too many masters (likely generated or merged); it masters the target and {skipped.OtherMasters.Length:N0} other mods.");
        }
        if (patches.Detected.Length == 0)
        {
            Console.WriteLine("Compatibility patches: none detected.");
            return;
        }
        foreach (var patch in patches.Detected)
        {
            Console.WriteLine($"  Compatibility patch {mods.KeyOf(patch.Patch)}: links target with {string.Join(", ", patch.OtherMasters.Select(mods.KeyOf))}.");
        }
        var ignoredMods = patches.IgnoredMods.Select(mods.KeyOf).OrderBy(mod => mod.ToString(), StringComparer.Ordinal);
        Console.WriteLine($"Ignored because of compatibility patches: {string.Join(", ", ignoredMods)}.");
    }

    private static string DescribeFollowUpRemoval(RunConfig config) => config.FollowUpMode switch
    {
        FollowUpRemovalMode.Nothing => "Also remove: nothing.",
        FollowUpRemovalMode.EverythingTouching => $"Also remove: everything touching, touch distance {config.TouchDistance}.",
        FollowUpRemovalMode.ObjectsSupportedByIt =>
            $"Also remove: objects supported by it, touch distance {config.TouchDistance}, threshold {config.AnchoringThreshold:P0}.",
        _ => throw new UnreachableException($"Unknown follow-up removal mode {config.FollowUpMode}."),
    };

    private static string DescribeLeftoverRemoval(RunConfig config)
    {
        var leftovers = config.Leftovers;
        if (!leftovers.Enabled) return "Leftover invisible objects: kept.";
        var protectedKinds = leftovers.ProtectedKinds.Count == 0 ? "none" : string.Join(", ", leftovers.ProtectedKinds.Order());
        return $"Leftover invisible objects: removed, search radius {leftovers.SearchRadius}, "
            + $"removed area per direction {leftovers.DirectionThresholdPercent}%,removed directions required {leftovers.RemovedDirectionsPercent}%, "
            + $"occupied directions required {leftovers.OccupiedDirectionsPercent}%, protected types {leftovers.ProtectedPreset} ({protectedKinds}), "
            + $"kept markers inside other mods' objects {(leftovers.MovesKeptMarkers ? "moved" : "left in place")}.";
    }

    public static void PrintOverriddenOthers(World world, ModKey target)
    {
        foreach (var other in world.OverriddenOthers)
        {
            Console.WriteLine(
                $"  Ignored other-mod object overridden by {target}: "
                + $"{RecordNames.Describe(other.FormKey, other.EditorId)} {RecordNames.DescribeOrigin(other.FormKey, other.WinningMod)}.");
        }
    }

    public static void PrintScanSummary(World world, ModKey target, TimeSpan elapsed)
    {
        var counts = world.Counts;
        Console.WriteLine(
            $"Scanned {counts.RecordsScanned:N0} placed records in {elapsed.TotalSeconds:F1}s: "
            + $"{world.Targets.Length:N0} {target} objects to check, "
            + $"{world.Rivals.Length:N0} objects from other mods.");
        if (counts.TargetsOverriddenLater > 0)
        {
            Console.WriteLine($"  Ignored {counts.TargetsOverriddenLater:N0} {target} objects whose winning version comes from a later plugin.");
        }
        if (counts.TargetsHiddenOrWithoutPlacement > 0)
        {
            Console.WriteLine($"  Ignored {counts.TargetsHiddenOrWithoutPlacement:N0} {target} objects that are initially disabled or have no valid position or rotation.");
        }
        if (counts.OtherInvalidPlacements > 0)
        {
            Console.WriteLine($"  Ignored {counts.OtherInvalidPlacements:N0} other-mod objects in the spaces of {target} objects whose position or rotation is out of range or not a number.");
        }
        if (counts.OthersOverriddenByTarget > 0)
        {
            Console.WriteLine($"  Ignored {counts.OthersOverriddenByTarget:N0} other-mod objects that {target} itself overrides.");
        }
        var supporterCount = world.Backdrop.IsCollected ? world.Rivals.Length + world.Backdrop.Value.Length : 0;
        if (supporterCount > 0)
        {
            Console.WriteLine($"  Recorded {supporterCount:N0} placed objects of any plugin as possible supporters or obstacles.");
        }
        if (counts.NavmeshCount > 0)
        {
            Console.WriteLine($"  Recorded {counts.NavmeshCount:N0} navmeshes.");
        }
    }

    /// <summary>The rivals of the target spaces, which the other steps look at.</summary>
    public static void PrintIndexSummary(World world, TimeSpan elapsed)
    {
        var targetSpaces = world.Targets.Select(target => target.SpaceKey).ToHashSet();
        var rivals = world.Rivals.Count(rival => targetSpaces.Contains(rival.SpaceKey));
        Console.WriteLine($"Indexed {rivals:N0} other objects in {targetSpaces.Count:N0} cells/worldspaces in {elapsed.TotalSeconds:F1}s.");
    }

    /// <summary>Per built world-bounds index, how long it took: of the rivals' spaces, and of the spaces of every plugin's objects (support and obstacles).</summary>
    public static void PrintBoundsIndexTimes(PhaseClock clock)
    {
        static string Describe(ImmutableArray<TimeSpan> buildTimes) =>
            $"{buildTimes.Length:N0} spaces in {buildTimes.Sum(time => time.TotalSeconds):F1}s";

        Console.WriteLine(
            $"World-bounds indexes built: other mods' objects {Describe(clock.Samples(TimedPhase.RivalBoundsBuild))}, "
            + $"supporters and obstacles {Describe(clock.Samples(TimedPhase.SolidBoundsBuild))}.");
    }

    public static void PrintWarmUpSummary(int targetBaseCount, TimeSpan elapsed) =>
        Console.WriteLine($"Bounds warm-up: {targetBaseCount:N0} target base objects in {elapsed.TotalSeconds:F1}s.");

    public static void PrintArchiveProblems(IEnumerable<ArchiveProblem> problems)
    {
        foreach (var problem in problems) Console.WriteLine(problem.Message);
    }

    public static void PrintAssetProblems(IEnumerable<AssetProblem> problems)
    {
        foreach (var problem in problems) Console.WriteLine(problem.Message);
    }

    public static void PrintReplacementLog(World world, Replacements replacements)
    {
        var entries = replacements.List
            .Select(replacement => (Replacement: replacement, Rival: world.Rivals[replacement.Rival.Index]))
            .OrderBy(entry => entry.Rival.FormKey.ToString(), StringComparer.Ordinal);
        foreach (var (replacement, rival) in entries)
        {
            var target = world.Targets[replacement.By.Index];
            Console.WriteLine(
                $"  Ignored replaced object {RecordNames.Describe(rival.FormKey, rival.EditorId)} from {rival.WinningMod}: "
                + $"replaced by {RecordNames.Describe(target.Key, target.EditorId)}, "
                + $"distance {replacement.Distance:F1}, size ratio {replacement.SizeRatio:F2}.");
        }
    }

    public static void PrintReplacementSummary(int replacedCount, TimeSpan elapsed) =>
        Console.WriteLine(
            $"Replacement matching: {replacedCount:N0} other-mod objects excluded in {elapsed.TotalSeconds:F1}s.");

    public static void PrintTooCloseSummary(int hitCount, int targetCount, ModKey target, TimeSpan elapsed) =>
        Console.WriteLine(
            $"Found {hitCount:N0} of {targetCount:N0} {target} objects too close to other mods' objects "
            + $"in {elapsed.TotalSeconds:F1}s.");

    public static void PrintShapeZoneStats(
        ShapeZoneWork stats,
        int largeOtherObjects,
        TriangleTreeStats meshes,
        int effectOnlyMeshes,
        TimeSpan indexTime,
        TimeSpan searchTime)
    {
        Console.WriteLine(
            $"Object-shape zones: {stats.CandidatePairs:N0} candidate pairs, {stats.BoxFilterPasses:N0} passed the box filter, "
            + $"{stats.NarrowTests:N0} mesh tests, {stats.Hits:N0} hits, {stats.CentrePointFallbacks:N0} centre-point fallbacks "
            + $"(other object without mesh triangles), {stats.BoxZoneTargets:N0} targets without mesh triangles used their box; "
            + $"{stats.TrianglePairsTested:N0} triangle pairs tested exactly.");
        Console.WriteLine(
            $"  {largeOtherObjects:N0} large other objects tested against every target of their space; "
            + $"{effectOnlyMeshes:N0} effect-only meshes (fog, light rays, water spray) ignored.");
        Console.WriteLine(
            $"  Meshes indexed: {meshes.Built:N0} ({meshes.Triangles:N0} triangles), peak resident ~{meshes.PeakResidentBytes / BytesPerMegabyte:N0} MB; "
            + $"timing: box index {indexTime.TotalSeconds:F1}s, search {searchTime.TotalSeconds:F1}s.");
    }

    public static void PrintIgnoredNpcs(int placedNpcCount) =>
        Console.WriteLine($"NPCs and creatures: ignored; {placedNpcCount:N0} placed NPCs of other mods never cause removals.");

    public static void PrintNpcStuckSummary(
        NpcStuckSummary summary,
        NpcBodyPerf bodies,
        bool detailedLog)
    {
        var cache = bodies.Cache;
        var sizes = summary.Sizes;
        Console.WriteLine(
            $"NPCs and creatures (only when stuck in the object): {sizes.Evaluated:N0} placed NPCs evaluated: "
            + $"{sizes.ByBodyMesh:N0} sized by body mesh, {sizes.ByObjectBounds:N0} by Object Bounds, "
            + $"{sizes.ByHumanoidApproximation:N0} by humanoid approximation, {sizes.ByPoint:N0} sized as a point; "
            + $"{sizes.WithoutNpc:N0} skipped because their base is no NPC; "
            + $"{summary.PairsTested:N0} NPC-object pairs tested ({summary.CoreTests:N0} body boxes), "
            + $"{summary.Conflicts:N0} objects with an NPC stuck in them.");
        Console.WriteLine(
            $"  NPC body cache: {cache.BodiesBuilt:N0} bodies built, {cache.BodiesReused:N0} reused; "
            + $"{cache.BasesResolved:N0} NPC bases resolved, {cache.BasesReused:N0} reused; "
            + $"{cache.ListsResolved:N0} leveled lists resolved, {cache.ListsReused:N0} reused; "
            + $"{bodies.BodyMeshSetsMeasured:N0} body mesh sets measured.");
        if (!detailedLog) return;
        foreach (var fallback in summary.PointFallbacks)
        {
            Console.WriteLine($"  Sized as a point {DescribeOtherObject(fallback.Npc)}: {fallback.Reason}.");
        }
    }

    public static void PrintInvisibleOthers(RivalCensus census, bool verbose)
    {
        var total = census.InvisibleByReason.Sum(kv => kv.Value);
        if (total == 0) return;
        Console.WriteLine($"  Ignored {total:N0} other-mod objects that are invisible (markers, lights, sounds, decals, trigger boxes, ...).");
        if (!verbose) return;
        foreach (var (reason, count) in census.InvisibleByReason)
        {
            Console.WriteLine($"    {reason}: {count:N0}");
        }
    }

    public static void PrintKept(World world, IEnumerable<KeptTarget> kept)
    {
        foreach (var entry in kept)
        {
            var target = world.Targets[entry.TargetIndex];
            var touched = entry.TouchedTargetIndex is { } touchedIndex
                ? $" (touches removed {RecordNames.Describe(world.Targets[touchedIndex])})"
                : string.Empty;
            Console.WriteLine($"  Kept {RecordNames.Describe(target)} in {world.SpaceNames[target.SpaceKey]}: {entry.Reason.Detail}{touched}.");
        }
    }

    /// <summary>Each kept target once, counted under the first reason found for it.</summary>
    public static void PrintKeptSummary(IEnumerable<KeptTarget> kept)
    {
        var byCategory = kept
            .DistinctBy(entry => entry.TargetIndex)
            .GroupBy(entry => entry.Reason.Category)
            .Select(group => (Category: group.Key, Count: group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Category, StringComparer.Ordinal)
            .ToList();
        Console.WriteLine($"Kept {byCategory.Sum(group => group.Count):N0} objects that would have been removed because other records depend on them.");
        foreach (var (category, count) in byCategory)
        {
            Console.WriteLine($"  {category}: {count:N0}");
        }
    }

    public static void PrintTouchStats(TouchClusters touch, TouchTimes times, TriangleTreeStats meshes)
    {
        var stats = touch.Stats;
        Console.WriteLine(
            $"Touching objects: {touch.Removals.Count(removal => removal is TouchingRemoval):N0} removed in {stats.Components:N0} components "
            + $"({stats.ComponentsWithRemovals:N0} with follow-up removals, largest {stats.LargestComponent:N0} removed objects, "
            + $"longest chain {stats.MaxDepth:N0} steps); {touch.Kept.Count:N0} kept as referenced.");
        PrintPairStats(stats.Pairs, stats.Levels, "levels");
        PrintMeshStats(meshes);
        Console.WriteLine(
            $"  Timing: setup {times.Setup.TotalSeconds:F1}s, broad phase {times.BroadPhase.TotalSeconds:F1}s, "
            + $"narrow phase {times.NarrowPhase.TotalSeconds:F1}s"
            + (touch.Diagnostics != null ? $", diagnostics edges {times.DiagnosticsEdges.TotalSeconds:F1}s." : "."));
    }

    public static void PrintAnchoringStats(AnchoringResult anchoring, AnchoringTimes times, TriangleTreeStats meshes)
    {
        var stats = anchoring.Stats;
        Console.WriteLine(
            $"Anchoring: {anchoring.Removals.Count(removal => removal is AnchoringRemoval):N0} removed over {stats.Iterations:N0} iterations; "
            + $"{stats.Candidates:N0} touching objects evaluated ({stats.Evaluations:N0} evaluations), "
            + $"{stats.KeptWithoutContacts:N0} kept without any contact points, {anchoring.Kept.Count:N0} kept as referenced.");
        PrintPairStats(stats.Pairs, stats.Iterations, "iterations");
        PrintMeshStats(meshes);
        Console.WriteLine(
            $"  Timing: setup {times.Setup.TotalSeconds:F1}s, touch search {times.TouchSearch.TotalSeconds:F1}s, "
            + $"contact points {times.ContactPoints.TotalSeconds:F1}s.");
    }

    public static void PrintLeftoverStats(LeftoverResult leftovers, TimeSpan elapsed)
    {
        var decisions = Enum.GetValues<LeftoverDecision>();
        Console.WriteLine(
            $"Leftover invisible objects: {leftovers.Evaluations.Count:N0} evaluated in {elapsed.TotalSeconds:F1}s; "
            + $"removed {leftovers.RemovedCount:N0} ({DescribeDecisionCounts(leftovers, decisions.Where(decision => decision.IsRemoval()))}); "
            + $"kept {leftovers.Evaluations.Count - leftovers.RemovedCount:N0} "
            + $"({DescribeDecisionCounts(leftovers, decisions.Where(decision => !decision.IsRemoval()))}).");
    }

    private static string DescribeDecisionCounts(LeftoverResult leftovers, IEnumerable<LeftoverDecision> decisions) =>
        string.Join(", ", decisions.Select(decision => $"{leftovers.CountDecisions(decision):N0} {decision.Describe()}"));

    public static void PrintLeftoverDecisions(World world, IEnumerable<LeftoverEvaluation> evaluations)
    {
        foreach (var evaluation in evaluations)
        {
            var target = world.Targets[evaluation.TargetIndex];
            Console.WriteLine(
                $"  Leftover {RecordNames.Describe(target)} ({evaluation.Kind}) in {world.SpaceNames[target.SpaceKey]}: "
                + $"{(evaluation.IsRemoved ? "removed" : "kept")}, {DescribeLeftoverReason(evaluation)}; radius {evaluation.Radius:F0}, "
                + $"removed/total ground area {evaluation.Surroundings.Describe()}.");
        }
    }

    /// <summary>The reason, naming the other mod's object the invisible object sits inside, if any.</summary>
    private static string DescribeLeftoverReason(LeftoverEvaluation evaluation) =>
        evaluation.ContainingObject is { } inside
            ? $"{evaluation.DescribeReason()} (inside {DescribeOtherObject(inside)})"
            : evaluation.DescribeReason();

    private static string DescribeOtherObject(OtherObject other) =>
        $"{RecordNames.Describe(other.FormKey, other.EditorId)} {RecordNames.DescribeOrigin(other.FormKey, other.WinningMod)}";

    public static void PrintRelocations(World world, RelocationResult relocations)
    {
        foreach (var move in relocations.Moved)
        {
            var target = world.Targets[move.Evaluation.TargetIndex];
            Console.WriteLine(
                $"  Moved kept {RecordNames.Describe(target)} in {world.SpaceNames[target.SpaceKey]} "
                + $"out of {DescribeOtherObject(move.Evaluation.ContainingObject!.Value)}: {move.Distance:F0} units onto the {move.Surface.ToString().ToLowerInvariant()}.");
            if (move.LeftHomeCell)
            {
                Console.WriteLine(
                    $"  Warning: {RecordNames.Describe(target)} was moved into the neighboring cell "
                    + $"({ExteriorGrid.CellIndex(move.To.X)}, {ExteriorGrid.CellIndex(move.To.Y)}) because its own cell has no free spot.");
            }
        }
        foreach (var evaluation in relocations.LeftInPlace)
        {
            var target = world.Targets[evaluation.TargetIndex];
            Console.WriteLine(
                $"  Left kept {RecordNames.Describe(target)} in {world.SpaceNames[target.SpaceKey]} "
                + $"inside {DescribeOtherObject(evaluation.ContainingObject!.Value)}: no free navmesh or terrain spot within {KeptObjectRelocator.MaxMoveDistance:F0} units.");
        }
        Console.WriteLine(
            $"Moved {relocations.Moved.Count:N0} kept markers out of other mods' objects; {relocations.LeftInPlace.Count:N0} left in place.");
    }

    private static void PrintPairStats(PairTestStats pairs, int rounds, string roundName) =>
        Console.WriteLine(
            $"  Pairs: {pairs.PairsTested:N0} box candidates next to a removal tested over {rounds:N0} {roundName}, "
            + $"{pairs.TouchingPairs:N0} touching, {pairs.PairsWithoutGeometry:N0} without mesh triangles (never touching); "
            + $"{pairs.TrianglePairsTested:N0} triangle pairs tested exactly.");

    private static void PrintMeshStats(TriangleTreeStats meshes) =>
        Console.WriteLine(
            $"  Meshes indexed: {meshes.Built:N0} ({meshes.Triangles:N0} triangles), {meshes.Rebuilt:N0} rebuilt, "
            + $"{meshes.Evicted:N0} evicted"
            + (meshes.TooLarge > 0 ? $", {meshes.TooLarge:N0} over {MeshTriangleTree.MaxTriangles:N0} triangles not used" : string.Empty)
            + $"; peak resident {meshes.PeakResidentMeshes:N0} meshes, ~{meshes.PeakResidentBytes / BytesPerMegabyte:N0} MB.");

    public static void PrintMeshOriginSummary(MeshOriginSummary summary) =>
        Console.WriteLine(
            $"Mesh origins: {summary.Meshes:N0} target meshes, {summary.NearBottom:N0} {MeshOriginDiagnosticsWriter.NearBottom}, "
            + $"{summary.NearCentre:N0} {MeshOriginDiagnosticsWriter.NearCentre}, {summary.Other:N0} {MeshOriginDiagnosticsWriter.Other}.");

    public static void PrintWriteSummary(int removedCount, int movedCount, int enableParentsReplaced, TimeSpan elapsed)
    {
        Console.WriteLine($"Wrote {removedCount + movedCount:N0} overrides ({removedCount:N0} removed, {movedCount:N0} moved) in {elapsed.TotalSeconds:F1}s.");
        if (enableParentsReplaced > 0)
        {
            Console.WriteLine(
                $"  {enableParentsReplaced:N0} removed objects had an Enable Parent; it was replaced by the player "
                + "with \"opposite of parent\" so they stay disabled.");
        }
    }

    public static void PrintRemovals(World world, IBaseFacts bases, IEnumerable<Removal> removals)
    {
        foreach (var removal in removals)
        {
            var target = world.Targets[removal.TargetIndex];
            Console.WriteLine(
                $"  Removed {RecordNames.Describe(target)} (base {RecordNames.DescribeBase(bases, target.Base)}) "
                + $"in {DescribeLocation(world, target)}; {DescribeRemovalReason(world, removal)}");
        }
    }

    private static string DescribeRemovalReason(World world, Removal removal) => removal switch
    {
        TooCloseRemoval { TooCloseTo: var other } => $"too close to {DescribeOtherObject(other)}",
        TouchingRemoval touching => $"touches removed {RecordNames.Describe(world.Targets[touching.TouchedTargetIndex])}",
        AnchoringRemoval anchoring =>
            $"{anchoring.RemovedShare:P0} of its support was removed (mostly {RecordNames.Describe(world.Targets[anchoring.MainRemovedSupporter])})",
        LeftoverRemoval { Evaluation: var evaluation } => $"invisible, {DescribeLeftoverReason(evaluation)}",
        LinkedRemoval linked => $"linked to removed {RecordNames.Describe(world.Targets[linked.LinkedToTargetIndex])}",
        _ => throw new UnreachableException($"Unknown removal type {removal.GetType().Name}."),
    };

    public static void PrintBoundsStats(BoundsStats stats)
    {
        Console.WriteLine(
            $"Bounds: {stats.BasesFromNif:N0} bases from NIF, {stats.BasesNifFallbackToObnd:N0} NIF misses, "
            + $"{stats.BasesFromObnd:N0} from OBND, {stats.BasesWithoutBounds + stats.BasesUnresolved:N0} without bounds. "
            + $"Meshes: {stats.ModelsRead:N0} read, {stats.ModelsEffectOnly:N0} effect-only, {stats.ModelsFailed:N0} failed "
            + $"({stats.ModelsFromLooseFiles:N0} loose, {stats.ModelsFromArchives:N0} from {stats.ArchivesIndexed:N0} archives), "
            + $"{stats.ModelsWithFooterRoot:N0} with a footer root other than their first node.");
        if (stats.ModelFailuresByKind.Count > 0)
        {
            Console.WriteLine(
                $"  Mesh failures: {string.Join(", ", stats.ModelFailuresByKind.Select(kv => $"{kv.Value:N0} {kv.Key}"))}.");
        }
    }

    public static void PrintSpaceSummary(World world, IReadOnlyList<Removal> removals)
    {
        var removedBySpace = removals
            .GroupBy(r => world.Targets[r.TargetIndex].SpaceKey)
            .ToDictionary(g => g.Key, g => g.Count());
        var rivalsBySpace = world.Rivals.CountBy(rival => rival.SpaceKey).ToDictionary();
        Console.WriteLine("Per cell/worldspace (target objects / other objects / removed):");
        foreach (var group in world.Targets
                     .GroupBy(t => t.SpaceKey)
                     .OrderBy(g => world.SpaceNames[g.Key], StringComparer.OrdinalIgnoreCase))
        {
            removedBySpace.TryGetValue(group.Key, out var removedCount);
            Console.WriteLine(
                $"  {world.SpaceNames[group.Key]}: {group.Count():N0} / {rivalsBySpace.GetValueOrDefault(group.Key):N0} / {removedCount:N0}");
        }
    }

    /// <param name="linkCount">Links from target objects to any target-plugin record, before those not between two target objects are dropped.</param>
    public static void PrintLinkedGroups(LinkedGroups groups, int linkCount)
    {
        var multiMember = groups.MultiMemberGroups.ToList();
        Console.WriteLine(
            $"Linked groups: {multiMember.Count:N0} groups of linked target objects ({multiMember.Sum(members => members.Count):N0} objects) "
            + $"from {linkCount:N0} links to target-plugin records.");
    }

    /// <param name="withoutPlacement">Target-plugin objects not checked because they are initially disabled or have no valid position.</param>
    public static void PrintTargetVisibility(IReadOnlyList<ObjectVisibility> visibility, int withoutPlacement)
    {
        var invisibleByKind = visibility
            .Where(entry => !entry.IsVisible)
            .GroupBy(entry => entry.Describe())
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Count():N0} {group.Key}")
            .ToList();
        var visible = visibility.Count(entry => entry.IsVisible);
        var kinds = invisibleByKind.Count == 0 ? "none" : string.Join(", ", invisibleByKind);
        Console.WriteLine(
            $"Target objects: {visible:N0} visible, {visibility.Count - visible:N0} invisible (by kind: {kinds}), "
            + $"{withoutPlacement:N0} without placement/disabled.");
    }

    public static void PrintLinkedRemovals(int count, string step) =>
        Console.WriteLine($"Linked groups: {count:N0} more objects removed with the {step} removals they are linked to.");

    public static void PrintRemovalSummary(IReadOnlyList<Removal> removals)
    {
        var tooClose = removals.Count(removal => removal is TooCloseRemoval);
        var followUp = removals.Count(removal => removal is TouchingRemoval or AnchoringRemoval);
        var leftovers = removals.Count(removal => removal is LeftoverRemoval);
        var linked = removals.Count(removal => removal is LinkedRemoval);
        Console.WriteLine(
            $"Removed {removals.Count:N0} objects ({tooClose:N0} too close, {followUp:N0} follow-up removals, "
            + $"{leftovers:N0} leftover invisible objects, {linked:N0} linked to removed objects).");
    }

    public static void PrintRemovedMarkersByType(IEnumerable<Removal> removals, IReadOnlyList<ObjectVisibility> visibility)
    {
        var counts = removals
            .Select(removal => visibility[removal.TargetIndex].Kind)
            .OfType<InvisibleObjectKind>()
            .GroupBy(kind => kind)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Count():N0} {group.Key}")
            .ToList();
        Console.WriteLine($"Removed markers by type: {(counts.Count == 0 ? "none" : string.Join(", ", counts))}.");
    }

    public static void PrintManualPatchHints(World world, IReadOnlyList<ManualPatchHint> hints)
    {
        Console.WriteLine($"Possible manual patch needed: {hints.Count:N0} objects to check.");
        foreach (var hint in hints)
        {
            var target = world.Targets[hint.TargetIndex];
            Console.WriteLine($"  {DescribeHintType(hint.Type)} {RecordNames.Describe(target)} in {DescribeLocation(world, target)}: {hint.Detail}.");
        }
    }

    private static string DescribeLocation(World world, TargetObject target)
    {
        var space = world.SpaceNames[target.SpaceKey];
        return target.CellName == null ? space : $"{space}, cell {target.CellName}";
    }

    private static string DescribeHintType(ManualPatchHintType type) => type switch
    {
        ManualPatchHintType.RemovedMarker => "Removed marker",
        ManualPatchHintType.KeptLinkedGroup => "Kept linked group of",
        ManualPatchHintType.KeptForNonPlacedReference => "Kept, referenced by a non-placed record:",
        ManualPatchHintType.KeptTeleportDoor => "Kept teleport door",
        _ => throw new UnreachableException($"Unknown manual patch hint type {type}."),
    };
}
