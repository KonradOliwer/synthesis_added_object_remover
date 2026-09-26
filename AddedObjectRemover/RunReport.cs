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
        Console.WriteLine($"Excluded plugins: {Join(config.ExcludedPlugins)}");
        Console.WriteLine(config.IgnoreTargetMasters
            ? $"Ignored masters of target: {Join(config.TargetMasters)}"
            : "Masters of target are not ignored.");
        PrintCompatibilityPatches(config.CompatibilityPatches);
        Console.WriteLine(DescribeFollowUpRemoval(config));
        Console.WriteLine(DescribeLeftoverRemoval(config));
        Console.WriteLine($"Detailed log: {config.DetailedLog}");
        Console.WriteLine($"Diagnostics folder: {(config.WritesDiagnostics ? config.DiagnosticsFolder : "(none)")}");
    }

    private static void PrintCompatibilityPatches(IReadOnlyList<CompatibilityPatch> patches)
    {
        if (patches.Count == 0)
        {
            Console.WriteLine("Compatibility patches: none detected.");
            return;
        }
        foreach (var patch in patches)
        {
            Console.WriteLine($"  Compatibility patch {patch.Patch}: links target with {string.Join(", ", patch.OtherMasters)}.");
        }
        var ignoredMods = CompatibilityPatchDetector.CollectIgnoredMods(patches).OrderBy(mod => mod.ToString(), StringComparer.Ordinal);
        Console.WriteLine($"Ignored because of compatibility patches: {string.Join(", ", ignoredMods)}.");
    }

    private static string DescribeFollowUpRemoval(RunConfig config) => config.FollowUpMode switch
    {
        FollowUpRemovalMode.Off => "Follow-up removal: off.",
        FollowUpRemovalMode.AnyTouch => $"Follow-up removal: any touch, touch distance {config.TouchDistance}.",
        FollowUpRemovalMode.Anchoring =>
            $"Follow-up removal: anchoring, touch distance {config.TouchDistance}, threshold {config.AnchoringThreshold:P0}.",
        _ => throw new UnreachableException($"Unknown follow-up removal mode {config.FollowUpMode}."),
    };

    private static string DescribeLeftoverRemoval(RunConfig config)
    {
        var leftovers = config.Leftovers;
        if (!leftovers.Enabled) return "Leftover invisible objects: kept.";
        var protectedKinds = leftovers.ProtectedKinds.Count == 0 ? "none" : string.Join(", ", leftovers.ProtectedKinds.Order());
        return $"Leftover invisible objects: removed, search radius {leftovers.SearchRadius}, "
            + $"direction threshold {leftovers.DirectionThresholdPercent}%, removed directions required {leftovers.RemovedDirectionsPercent}%, "
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
            Console.WriteLine($"  Ignored {scan.TargetsDisabledOrWithoutPlacement:N0} {target} objects that are initially disabled or have no valid position.");
        }
        if (scan.OthersOverriddenByTarget > 0)
        {
            Console.WriteLine($"  Ignored {scan.OthersOverriddenByTarget:N0} other-mod objects that {target} itself overrides.");
        }
        if (scan.SupportersBySpace.Count > 0)
        {
            Console.WriteLine($"  Recorded {scan.SupporterCount:N0} placed objects of any plugin as possible supporters or obstacles.");
        }
        if (scan.NavmeshTrianglesBySpace.Count > 0)
        {
            Console.WriteLine($"  Recorded {scan.NavmeshTrianglesBySpace.Values.Sum(triangles => triangles.Count):N0} navmesh triangles.");
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
            + $"removed {leftovers.Removals.Count:N0} ({DescribeDecisionCounts(leftovers, decisions.Where(LeftoverDecisionText.IsRemoval))}); "
            + $"kept {leftovers.Evaluations.Count - leftovers.Removals.Count:N0} "
            + $"({DescribeDecisionCounts(leftovers, decisions.Where(decision => !LeftoverDecisionText.IsRemoval(decision)))}).");
    }

    private static string DescribeDecisionCounts(LeftoverResult leftovers, IEnumerable<LeftoverDecision> decisions) =>
        string.Join(", ", decisions.Select(decision => $"{leftovers.CountDecisions(decision):N0} {LeftoverDecisionText.Describe(decision)}"));

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
            ? $"{LeftoverDecisionText.DescribeReason(evaluation)} (inside {DescribeOtherObject(inside)})"
            : LeftoverDecisionText.DescribeReason(evaluation);

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
            $"Moved {relocations.Moved.Count:N0} kept invisible objects out of other mods' objects; {relocations.LeftInPlace.Count:N0} left in place.");
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
            var space = scan.SpaceNames[target.SpaceKey];
            var location = target.CellName == null ? space : $"{space}, cell {target.CellName}";
            Console.WriteLine(
                $"  Removed {RecordNames.Describe(target.Record)} (base {RecordNames.DescribeBase(shapes, target.Base)}) "
                + $"in {location}; {DescribeRemovalReason(scan, removal)}");
        }
    }

    private static string DescribeRemovalReason(ScanResult scan, Removal removal) => removal switch
    {
        TooCloseRemoval { TooCloseTo: var other } => $"too close to {DescribeOtherObject(other)}",
        TouchingRemoval touching => $"touches removed {RecordNames.Describe(scan.Targets[touching.TouchedTargetIndex].Record)}",
        AnchoringRemoval anchoring =>
            $"{anchoring.RemovedShare:P0} of its support was removed (mostly {RecordNames.Describe(scan.Targets[anchoring.MainRemovedSupporter].Record)})",
        LeftoverRemoval { Evaluation: var evaluation } => $"invisible, {DescribeLeftoverReason(evaluation)}",
        _ => throw new UnreachableException($"Unknown removal type {removal.GetType().Name}."),
    };

    public static void PrintBoundsStats(BoundsStats stats)
    {
        Console.WriteLine(
            $"Bounds: {stats.BasesFromNif:N0} bases from NIF, {stats.BasesNifFallbackToObnd:N0} NIF misses, "
            + $"{stats.BasesFromObnd:N0} from OBND, {stats.BasesWithoutBounds + stats.BasesUnresolved:N0} without bounds. "
            + $"Meshes: {stats.ModelsRead:N0} read, {stats.ModelsFailed:N0} failed "
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

    public static void PrintRemovalSummary(int removedCount, int removedTooClose, int removedLeftovers) =>
        Console.WriteLine(
            $"Removed {removedCount:N0} objects ({removedTooClose:N0} too close, "
            + $"{removedCount - removedTooClose - removedLeftovers:N0} follow-up removals, "
            + $"{removedLeftovers:N0} leftover invisible objects).");
}
