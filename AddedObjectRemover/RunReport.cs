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
        Console.WriteLine(DescribeFollowUpRemoval(config));
        Console.WriteLine(config.RemoveOrphanedInvisibleObjects
            ? $"Orphaned invisible objects: removed, check radius {config.OrphanCheckRadius}, removed share {config.OrphanRemovedShare:P0}."
            : "Orphaned invisible objects: kept.");
        Console.WriteLine($"Detailed log: {config.DetailedLog}");
        Console.WriteLine($"Diagnostics folder: {(config.WritesDiagnostics ? config.DiagnosticsFolder : "(none)")}");
    }

    private static string DescribeFollowUpRemoval(RunConfig config) => config.FollowUpMode switch
    {
        FollowUpRemovalMode.Off => "Follow-up removal: off.",
        FollowUpRemovalMode.AnyTouch => $"Follow-up removal: any touch, touch distance {config.TouchDistance}.",
        FollowUpRemovalMode.Anchoring =>
            $"Follow-up removal: anchoring, touch distance {config.TouchDistance}, threshold {config.AnchoringThreshold:P0}.",
        _ => throw new UnreachableException($"Unknown follow-up removal mode {config.FollowUpMode}."),
    };

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
            Console.WriteLine($"  Recorded {scan.SupporterCount:N0} placed objects of any plugin as possible Anchoring supporters.");
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
            Console.WriteLine($"  Kept {RecordNames.Describe(target.Record)} in {scan.SpaceNames[target.SpaceKey]}: {entry.Reason}{touched}.");
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

    public static void PrintOrphanStats(OrphanResult orphans, TimeSpan elapsed) =>
        Console.WriteLine(
            $"Orphaned invisible objects: {orphans.Evaluations.Count:N0} evaluated, {orphans.Removals.Count:N0} removed in {elapsed.TotalSeconds:F1}s; kept "
            + $"{orphans.CountDecisions(OrphanDecision.KeptNoSceneryNearby):N0} with no scenery nearby, "
            + $"{orphans.CountDecisions(OrphanDecision.KeptNotAllSidesCleared):N0} with not all sides cleared, "
            + $"{orphans.CountDecisions(OrphanDecision.KeptShareBelowThreshold):N0} with removed share below threshold, "
            + $"{orphans.CountDecisions(OrphanDecision.KeptReferenced):N0} as referenced.");

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

    public static void PrintWriteSummary(int overrideCount, int enableParentsReplaced, TimeSpan elapsed)
    {
        Console.WriteLine($"Wrote {overrideCount:N0} overrides in {elapsed.TotalSeconds:F1}s.");
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
        TooCloseRemoval { TooCloseTo: var other } =>
            $"too close to {RecordNames.Describe(other.FormKey, other.EditorId)} {RecordNames.DescribeOrigin(other.FormKey, other.WinningMod)}",
        TouchingRemoval touching => $"touches removed {RecordNames.Describe(scan.Targets[touching.TouchedTargetIndex].Record)}",
        AnchoringRemoval anchoring =>
            $"{anchoring.RemovedShare:P0} of its support was removed (mostly {RecordNames.Describe(scan.Targets[anchoring.MainRemovedSupporter].Record)})",
        OrphanRemoval { Neighbours: var neighbours } =>
            $"invisible and {neighbours.RemovedShare:P0} of the visible objects around it were removed "
            + $"({neighbours.TotalRemoved:N0}/{neighbours.TotalVisible:N0}; {neighbours.Describe()})",
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

    public static void PrintRemovalSummary(int removedCount, int removedTooClose, int removedOrphans, int keptAsReferenced) =>
        Console.WriteLine(
            $"Removed {removedCount:N0} objects ({removedTooClose:N0} too close, "
            + $"{removedCount - removedTooClose - removedOrphans:N0} follow-up removals, "
            + $"{removedOrphans:N0} orphaned invisible objects); "
            + $"kept {keptAsReferenced:N0} referenced objects.");
}
