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
        Console.WriteLine($"Multiplier: {config.Multiplier}");
        Console.WriteLine($"Excluded plugins: {Join(config.ExcludedPlugins)}");
        Console.WriteLine(config.ExcludeTargetMasters
            ? $"Excluded masters of target: {Join(config.TargetMasters)}"
            : "Masters of target are not excluded.");
        Console.WriteLine($"Bounds source: {(config.UseNifBounds ? "NIF mesh, OBND fallback" : "OBND only")}");
        Console.WriteLine($"Keep referenced objects: {config.KeepReferencedObjects}; verbose: {config.Verbose}");
        Console.WriteLine(config.RemoveTouching
            ? $"Remove touching objects: tolerance {config.TouchTolerance}"
            : "Touching objects are not removed.");
        Console.WriteLine(config.IgnoreReplacedObjects
            ? $"Ignore replaced objects: position tolerance {config.ReplacementPositionTolerance}, size similarity {config.ReplacementSizeSimilarity}"
            : "Same-position replacement matching is disabled (objects the target plugin itself overrides are still ignored).");
        Console.WriteLine(string.IsNullOrEmpty(config.TouchDiagnosticsFile)
            ? "Touch diagnostics file: (none)"
            : $"Touch diagnostics file: {config.TouchDiagnosticsFile}.edges.csv / .components.csv");
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
        var meshes = stats.Meshes;
        Console.WriteLine(
            $"  Pairs: {stats.CandidatePairs:N0} box candidates, {stats.PairsTested:N0} tested (reachable from a removal), "
            + $"{stats.TouchingPairs:N0} touching, {stats.PairsWithoutGeometry:N0} without mesh geometry (never touching); "
            + $"{stats.TrianglePairsTested:N0} triangle pairs tested exactly.");
        Console.WriteLine(
            $"  Meshes indexed: {meshes.Built:N0} ({meshes.Triangles:N0} triangles), {meshes.Rebuilt:N0} rebuilt, "
            + $"{meshes.Evicted:N0} evicted early"
            + (meshes.TooLarge > 0 ? $", {meshes.TooLarge:N0} over {MeshTriangleTree.MaxTriangles:N0} triangles not used" : string.Empty)
            + $"; peak resident {meshes.PeakResidentMeshes:N0} meshes, ~{meshes.PeakResidentBytes / BytesPerMegabyte:N0} MB.");
        Console.WriteLine(
            $"  Timing: setup {stats.Setup.TotalSeconds:F1}s, broad phase {stats.BroadPhase.TotalSeconds:F1}s, "
            + $"narrow phase {stats.NarrowPhase.TotalSeconds:F1}s, clusters {stats.Clusters.TotalSeconds:F1}s.");
    }

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
        _ => throw new UnreachableException($"Unknown removal type {removal.GetType().Name}."),
    };

    public static void PrintBoundsStats(BoundsStats stats, bool useNif)
    {
        if (useNif)
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
        else
        {
            Console.WriteLine(
                $"Bounds: {stats.BasesFromObnd:N0} bases from OBND, "
                + $"{stats.BasesWithoutBounds + stats.BasesUnresolved:N0} without bounds.");
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

    public static void PrintRemovalSummary(int removedCount, int removedTooClose, int keptAsReferenced) =>
        Console.WriteLine(
            $"Removed {removedCount:N0} objects ({removedTooClose:N0} too close, "
            + $"{removedCount - removedTooClose:N0} touching a removed object); "
            + $"kept {keptAsReferenced:N0} referenced objects.");
}
