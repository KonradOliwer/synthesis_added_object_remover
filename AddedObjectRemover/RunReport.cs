using System.Diagnostics;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>Console output of a run: configuration, per-phase summaries, verbose details and final statistics.</summary>
internal static class RunReport
{
    private const double BytesPerMegabyte = 1024.0 * 1024.0;

    public static void PrintConfig(RunConfig config)
    {
        static string Join(IReadOnlyList<ModKey> keys) => keys.Count == 0 ? "(none)" : string.Join(", ", keys);

        Console.WriteLine($"Target plugin: {config.Target}");
        Console.WriteLine($"Size multiplier: {config.SizeMultiplier}");
        Console.WriteLine($"Removal zone: {config.ZoneShape}");
        Console.WriteLine($"Excluded plugins: {Join(config.ExcludedPlugins)}");
        Console.WriteLine(config.IgnoreTargetMasters
            ? $"Ignored masters of target: {Join(config.TargetMasters)}"
            : "Masters of target are not ignored.");
        PrintCompatibilityPatches(config.CompatibilityPatches);
        Console.WriteLine($"NPCs and creatures: {config.NpcHandling}");
        Console.WriteLine(DescribeFollowUpRemoval(config));
        Console.WriteLine(DescribeLeftoverRemoval(config));
        Console.WriteLine($"Detailed log: {config.DetailedLog}");
        Console.WriteLine($"Write report files: {config.WritesDiagnostics}");
        Console.WriteLine($"Report folder: {config.DiagnosticsFolder}");
    }

    private static void PrintCompatibilityPatches(CompatibilityPatches patches)
    {
        if (ReferenceEquals(patches, CompatibilityPatches.None))
        {
            Console.WriteLine("Compatibility patches: detection off.");
            return;
        }
        Console.WriteLine($"Compatibility patch detection: {patches.PluginsMasteringTarget:N0} plugins master the target.");
        foreach (var skipped in patches.SkippedTooManyMasters)
        {
            Console.WriteLine(
                $"  Plugin {skipped.Patch} skipped: too many masters (likely generated or merged); it masters the target and {skipped.OtherMasters.Count:N0} other mods.");
        }
        if (patches.Patches.Count == 0)
        {
            Console.WriteLine("Compatibility patches: none detected.");
            return;
        }
        foreach (var patch in patches.Patches)
        {
            Console.WriteLine($"  Compatibility patch {patch.Patch}: links target with {string.Join(", ", patch.OtherMasters)}.");
        }
        var ignoredMods = patches.CollectIgnoredMods().OrderBy(mod => mod.ToString(), StringComparer.Ordinal);
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

    public static void PrintOverriddenOthers(ScanResult scan, ModKey target)
    {
        foreach (var other in scan.OverriddenOthersLog)
        {
            Console.WriteLine(
                $"  Ignored other-mod object overridden by {target}: "
                + $"{RecordNames.Describe(other.FormKey, other.EditorId)} {RecordNames.DescribeOrigin(other.FormKey, other.WinningMod)}.");
        }
    }

    public static void PrintScanSummary(ScanResult scan, ModKey target, TimeSpan elapsed)
    {
        Console.WriteLine(
            $"Scanned {scan.RecordsScanned:N0} placed records in {elapsed.TotalSeconds:F1}s: "
            + $"{scan.Targets.Count:N0} {target} objects to check, "
            + $"{scan.OtherObjectCount:N0} objects from other mods.");
        if (scan.TargetsOverriddenLater > 0)
        {
            Console.WriteLine($"  Ignored {scan.TargetsOverriddenLater:N0} {target} objects whose winning version comes from a later plugin.");
        }
        if (scan.TargetsDisabledOrWithoutPlacement > 0)
        {
            Console.WriteLine($"  Ignored {scan.TargetsDisabledOrWithoutPlacement:N0} {target} objects that are initially disabled or have no valid position or rotation.");
        }
        if (scan.InvalidPlacements > 0)
        {
            Console.WriteLine($"  Ignored {scan.InvalidPlacements:N0} placed objects in the spaces of {target} objects whose position or rotation is out of range or not a number.");
        }
        if (scan.OthersOverriddenByTarget > 0)
        {
            Console.WriteLine($"  Ignored {scan.OthersOverriddenByTarget:N0} other-mod objects that {target} itself overrides.");
        }
        if (scan.SupportersBySpace.Count > 0)
        {
            Console.WriteLine($"  Recorded {scan.SupporterCount:N0} placed objects of any plugin as possible supporters or obstacles.");
        }
        if (scan.NavmeshesBySpace.Count > 0)
        {
            Console.WriteLine($"  Recorded {scan.NavmeshCount:N0} navmeshes.");
        }
    }

    public static void PrintIndexSummary(IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes, TimeSpan elapsed) =>
        Console.WriteLine(
            $"Indexed {indexes.Values.Sum(s => s.Count):N0} other objects in {indexes.Count:N0} cells/worldspaces "
            + $"in {elapsed.TotalSeconds:F1}s.");

    public static void PrintWarmUpSummary(int targetBaseCount, TimeSpan elapsed) =>
        Console.WriteLine($"Bounds warm-up: {targetBaseCount:N0} target base objects in {elapsed.TotalSeconds:F1}s.");

    public static void PrintReplacementLog(IEnumerable<ReplacementLogEntry> entries)
    {
        foreach (var entry in entries.OrderBy(e => e.OtherFormKey.ToString(), StringComparer.Ordinal))
        {
            Console.WriteLine(
                $"  Ignored replaced object {RecordNames.Describe(entry.OtherFormKey, entry.OtherEditorId)} from {entry.OtherPlugin}: "
                + $"replaced by {RecordNames.Describe(entry.TargetFormKey, entry.TargetEditorId)}, "
                + $"distance {entry.Distance:F1}, size ratio {entry.SizeRatio:F2}.");
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
        ShapeZoneStats stats,
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
        NpcBodyCacheStats cache,
        IReadOnlyList<SkinnedBodyMeasurement> bodyMeasurements,
        bool detailedLog)
    {
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
            + $"{bodyMeasurements.Count:N0} body mesh sets measured.");
        if (!detailedLog) return;
        foreach (var measurement in bodyMeasurements)
        {
            Console.WriteLine($"  {DescribeBodyMeasurement(measurement)}");
        }
        foreach (var fallback in summary.PointFallbacks)
        {
            Console.WriteLine($"  Sized as a point {DescribeOtherObject(fallback.Npc)}: {fallback.Reason}.");
        }
    }

    private static string DescribeBodyMeasurement(SkinnedBodyMeasurement measurement)
    {
        var raw = measurement.Size.Raw.Size;
        var body = measurement.Size.Body.Size;
        return $"Body mesh {measurement.Meshes}: raw {raw.X:F0} wide × {raw.Y:F0} deep, "
            + $"waist-based {body.X:F0} wide × {body.Y:F0} deep, {body.Z:F0} tall (units before race and NPC height).";
    }

    public static void PrintInvisibleOthers(ReasonCounter invisible, bool verbose)
    {
        var byReason = invisible.Snapshot();
        var total = byReason.Sum(kv => kv.Value);
        if (total == 0) return;
        Console.WriteLine($"  Ignored {total:N0} nearby other-mod objects that are invisible (markers, lights, sounds, decals, trigger boxes, ...).");
        if (!verbose) return;
        foreach (var (reason, count) in byReason)
        {
            Console.WriteLine($"    {reason}: {count:N0}");
        }
    }

    public static void PrintKept(ScanResult scan, IEnumerable<KeptTarget> kept)
    {
        foreach (var entry in kept)
        {
            var target = scan.Targets[entry.TargetIndex];
            var touched = entry.TouchedTargetIndex is { } touchedIndex
                ? $" (touches removed {RecordNames.Describe(scan.Targets[touchedIndex].Record)})"
                : string.Empty;
            Console.WriteLine($"  Kept {RecordNames.Describe(target.Record)} in {scan.SpaceNames[target.SpaceKey]}: {entry.Reason.Detail}{touched}.");
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

    public static void PrintTouchStats(TouchClusters touch)
    {
        var stats = touch.Stats;
        Console.WriteLine(
            $"Touching objects: {touch.Removals.Count:N0} removed in {stats.Components:N0} components "
            + $"({stats.ComponentsWithTouching:N0} with touching objects, largest {stats.LargestComponent:N0} removed objects); "
            + $"{touch.Kept.Count:N0} kept as referenced.");
        PrintPairStats(stats.Pairs, stats.Levels, "levels");
        PrintMeshStats(stats.Pairs.Meshes);
        Console.WriteLine(
            $"  Timing: setup {stats.Setup.TotalSeconds:F1}s, broad phase {stats.BroadPhase.TotalSeconds:F1}s, "
            + $"narrow phase {stats.NarrowPhase.TotalSeconds:F1}s"
            + (touch.Diagnostics != null ? $", diagnostics edges {stats.DiagnosticsEdges.TotalSeconds:F1}s." : "."));
    }

    public static void PrintAnchoringStats(AnchoringResult anchoring)
    {
        var stats = anchoring.Stats;
        Console.WriteLine(
            $"Anchoring: {anchoring.Removals.Count:N0} removed over {stats.Iterations:N0} iterations; "
            + $"{stats.Candidates:N0} touching objects evaluated ({stats.Evaluations:N0} evaluations), "
            + $"{stats.KeptWithoutContacts:N0} kept without any contact points, {anchoring.Kept.Count:N0} kept as referenced.");
        PrintPairStats(stats.Pairs, stats.Iterations, "iterations");
        PrintMeshStats(stats.Pairs.Meshes);
        Console.WriteLine(
            $"  Timing: setup {stats.Setup.TotalSeconds:F1}s, touch search {stats.TouchSearch.TotalSeconds:F1}s, "
            + $"contact points {stats.ContactPoints.TotalSeconds:F1}s.");
    }

    public static void PrintLeftoverStats(LeftoverResult leftovers, TimeSpan elapsed)
    {
        var decisions = Enum.GetValues<LeftoverDecision>();
        Console.WriteLine(
            $"Leftover invisible objects: {leftovers.Evaluations.Count:N0} evaluated in {elapsed.TotalSeconds:F1}s; "
            + $"removed {leftovers.Removals.Count:N0} ({DescribeDecisionCounts(leftovers, decisions.Where(decision => decision.IsRemoval()))}); "
            + $"kept {leftovers.Evaluations.Count - leftovers.Removals.Count:N0} "
            + $"({DescribeDecisionCounts(leftovers, decisions.Where(decision => !decision.IsRemoval()))}).");
    }

    private static string DescribeDecisionCounts(LeftoverResult leftovers, IEnumerable<LeftoverDecision> decisions) =>
        string.Join(", ", decisions.Select(decision => $"{leftovers.CountDecisions(decision):N0} {decision.Describe()}"));

    public static void PrintLeftoverDecisions(ScanResult scan, IEnumerable<LeftoverEvaluation> evaluations)
    {
        foreach (var evaluation in evaluations)
        {
            var target = scan.Targets[evaluation.TargetIndex];
            Console.WriteLine(
                $"  Leftover {RecordNames.Describe(target.Record)} ({evaluation.Kind}) in {scan.SpaceNames[target.SpaceKey]}: "
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

    public static void PrintRelocations(ScanResult scan, RelocationResult relocations)
    {
        foreach (var move in relocations.Moved)
        {
            var target = scan.Targets[move.Evaluation.TargetIndex];
            Console.WriteLine(
                $"  Moved kept {RecordNames.Describe(target.Record)} in {scan.SpaceNames[target.SpaceKey]} "
                + $"out of {DescribeOtherObject(move.Evaluation.ContainingObject!.Value)}: {move.Distance:F0} units onto the {move.Surface.ToString().ToLowerInvariant()}.");
        }
        foreach (var evaluation in relocations.LeftInPlace)
        {
            var target = scan.Targets[evaluation.TargetIndex];
            Console.WriteLine(
                $"  Left kept {RecordNames.Describe(target.Record)} in {scan.SpaceNames[target.SpaceKey]} "
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

    public static void PrintRemovals(ScanResult scan, BaseObjectShapeProvider shapes, IEnumerable<Removal> removals)
    {
        foreach (var removal in removals)
        {
            var target = scan.Targets[removal.TargetIndex];
            Console.WriteLine(
                $"  Removed {RecordNames.Describe(target.Record)} (base {RecordNames.DescribeBase(shapes, target.Base)}) "
                + $"in {DescribeLocation(scan, target)}; {DescribeRemovalReason(scan, removal)}");
        }
    }

    private static string DescribeRemovalReason(ScanResult scan, Removal removal) => removal switch
    {
        TooCloseRemoval { TooCloseTo: var other } => $"too close to {DescribeOtherObject(other)}",
        TouchingRemoval touching => $"touches removed {RecordNames.Describe(scan.Targets[touching.TouchedTargetIndex].Record)}",
        AnchoringRemoval anchoring =>
            $"{anchoring.RemovedShare:P0} of its support was removed (mostly {RecordNames.Describe(scan.Targets[anchoring.MainRemovedSupporter].Record)})",
        LeftoverRemoval { Evaluation: var evaluation } => $"invisible, {DescribeLeftoverReason(evaluation)}",
        LinkedRemoval linked => $"linked to removed {RecordNames.Describe(scan.Targets[linked.LinkedToTargetIndex].Record)}",
        _ => throw new UnreachableException($"Unknown removal type {removal.GetType().Name}."),
    };

    public static void PrintBoundsStats(BoundsStats stats)
    {
        Console.WriteLine(
            $"Bounds: {stats.BasesFromNif:N0} bases from NIF, {stats.BasesNifFallbackToObnd:N0} NIF misses, "
            + $"{stats.BasesFromObnd:N0} from OBND, {stats.BasesWithoutBounds + stats.BasesUnresolved:N0} without bounds. "
            + $"Meshes: {stats.ModelsRead:N0} read, {stats.ModelsEffectOnly:N0} effect-only, {stats.ModelsFailed:N0} failed "
            + $"({stats.ModelsFromLooseFiles:N0} loose, {stats.ModelsFromArchives:N0} from {stats.ArchivesIndexed:N0} archives).");
        if (stats.ModelFailuresByKind.Count > 0)
        {
            Console.WriteLine(
                $"  Mesh failures: {string.Join(", ", stats.ModelFailuresByKind.Select(kv => $"{kv.Value:N0} {kv.Key}"))}.");
        }
    }

    public static void PrintSpaceSummary(
        ScanResult scan,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        IReadOnlyList<Removal> removals)
    {
        var removedBySpace = removals
            .GroupBy(r => scan.Targets[r.TargetIndex].SpaceKey)
            .ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine("Per cell/worldspace (target objects / other objects / removed):");
        foreach (var group in scan.Targets
                     .GroupBy(t => t.SpaceKey)
                     .OrderBy(g => scan.SpaceNames[g.Key], StringComparer.OrdinalIgnoreCase))
        {
            removedBySpace.TryGetValue(group.Key, out var removedCount);
            Console.WriteLine(
                $"  {scan.SpaceNames[group.Key]}: {group.Count():N0} / {indexes[group.Key].Count:N0} / {removedCount:N0}");
        }
    }

    /// <param name="linkCount">Links from target objects to other target-plugin records, before those not between two target objects are dropped.</param>
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

    public static void PrintManualPatchHints(ScanResult scan, IReadOnlyList<ManualPatchHint> hints)
    {
        Console.WriteLine($"Possible manual patch needed: {hints.Count:N0} objects to check.");
        foreach (var hint in hints)
        {
            var target = scan.Targets[hint.TargetIndex];
            Console.WriteLine($"  {DescribeHintType(hint.Type)} {RecordNames.Describe(target.Record)} in {DescribeLocation(scan, target)}: {hint.Detail}.");
        }
    }

    private static string DescribeLocation(ScanResult scan, TargetObject target)
    {
        var space = scan.SpaceNames[target.SpaceKey];
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
