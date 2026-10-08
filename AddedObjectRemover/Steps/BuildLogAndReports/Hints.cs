using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>
/// Collects the objects to check by hand after a run: removed markers that actors or the map
/// use (door, map, idle and furniture markers), and objects that would have been removed but were
/// kept, grouped by why they stay. Ordered by type, then record key, then detail.
/// </summary>
internal sealed class ManualPatchHints(
    IReadOnlyList<TargetObject> targets,
    IBaseObjectShapes shapes,
    IObjectsToKeep protection)
{
    private static readonly ImmutableHashSet<InvisibleObjectKind> UsedMarkerKinds =
    [
        InvisibleObjectKind.DoorMarkers,
        InvisibleObjectKind.MapMarkers,
        InvisibleObjectKind.IdleMarkers,
        InvisibleObjectKind.FurnitureMarkers,
    ];

    public static ImmutableArray<ManualPatchHint> Hints(StepResults decided, IBaseObjectShapes shapes, IEnumerable<RemovedObject> removals) =>
        [.. new ManualPatchHints(decided.World.Targets, shapes, decided.Protection).Collect(removals, RemovalList.Kept(decided.Final))];

    public List<ManualPatchHint> Collect(IEnumerable<RemovedObject> removals, IEnumerable<KeptObject> kept)
    {
        var keptIndices = kept.Select(entry => entry.TargetIndex).Distinct().ToList();
        return CollectRemovedMarkers(removals)
            .Concat(CollectKeptLinkedGroups(keptIndices))
            .Concat(CollectKeptByOwnReason(keptIndices, KeepKind.NonPlacedReference, ManualPatchHintType.KeptForNonPlacedReference))
            .Concat(CollectKeptByOwnReason(keptIndices, KeepKind.TeleportDoor, ManualPatchHintType.KeptTeleportDoor))
            .OrderBy(hint => hint.Type)
            .ThenBy(hint => targets[hint.TargetIndex].Key, RecordKeyTextOrder.Comparer)
            .ThenBy(hint => hint.Detail, StringComparer.Ordinal)
            .ToList();
    }

    private IEnumerable<ManualPatchHint> CollectRemovedMarkers(IEnumerable<RemovedObject> removals) =>
        from removal in removals
        let kind = shapes.VisibilityOf(targets[removal.TargetIndex]).Kind
        where kind is { } markerKind && UsedMarkerKinds.Contains(markerKind)
        select new ManualPatchHint(ManualPatchHintType.RemovedMarker, removal.TargetIndex, $"{kind}");

    private IEnumerable<ManualPatchHint> CollectKeptLinkedGroups(IEnumerable<int> keptIndices) =>
        keptIndices
            .Where(protection.Groups.IsLinked)
            .Select(protection.Groups.MembersOf)
            .DistinctBy(members => members[0])
            .Select(members => new ManualPatchHint(ManualPatchHintType.KeptLinkedGroup, members[0], DescribeKeptGroup(members)));

    private string DescribeKeptGroup(IReadOnlyList<int> members)
    {
        var names = members.Select(member => RecordNames.Describe(targets[member]));
        var reasons = WithOwnReasons(members).Select(entry => $"{RecordNames.Describe(targets[entry.Index])}: {KeepReasonText.Detail(entry.Reason)}");
        return $"members {string.Join(", ", names)}; kept because {string.Join("; ", reasons)}";
    }

    private IEnumerable<ManualPatchHint> CollectKeptByOwnReason(IEnumerable<int> keptIndices, KeepKind kind, ManualPatchHintType type) =>
        WithOwnReasons(keptIndices)
            .Where(entry => entry.Reason.Kind == kind)
            .Select(entry => new ManualPatchHint(type, entry.Index, KeepReasonText.Detail(entry.Reason)));

    /// <returns>The objects that must stay themselves, with their reason.</returns>
    private IEnumerable<(int Index, KeepReason Reason)> WithOwnReasons(IEnumerable<int> indices) =>
        indices
            .Select(index => (Index: index, Reason: protection.GetOwnReason(index)))
            .Where(entry => entry.Reason != null)
            .Select(entry => (entry.Index, entry.Reason!));
}
