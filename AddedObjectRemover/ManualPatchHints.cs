namespace AddedObjectRemover;

internal enum ManualPatchHintType
{
    RemovedMarker,
    KeptLinkedGroup,
    KeptForNonPlacedReference,
    KeptTeleportDoor,
}

/// <summary>A removed or kept target object whose surroundings may need a manual patch.</summary>
/// <param name="TargetIndex">The object; for a linked group its first member.</param>
internal sealed record ManualPatchHint(ManualPatchHintType Type, int TargetIndex, string Detail);

/// <summary>
/// Collects the objects to check by hand after a run: removed markers that actors or the map
/// use (door, map, idle and furniture markers), and objects that would have been removed but were
/// kept, grouped by why they stay. Ordered by type, then FormKey, then detail.
/// </summary>
internal sealed class ManualPatchHints(
    IReadOnlyList<TargetObject> targets,
    IReadOnlyList<ObjectVisibility> visibility,
    LinkedGroups groups,
    KeepReferencedRule keepRule)
{
    private static readonly HashSet<InvisibleObjectKind> UsedMarkerKinds =
    [
        InvisibleObjectKind.DoorMarkers,
        InvisibleObjectKind.MapMarkers,
        InvisibleObjectKind.IdleMarkers,
        InvisibleObjectKind.FurnitureMarkers,
    ];

    public List<ManualPatchHint> Collect(IEnumerable<Removal> removals, IEnumerable<KeptTarget> kept)
    {
        var keptIndices = kept.Select(entry => entry.TargetIndex).Distinct().ToList();
        return CollectRemovedMarkers(removals)
            .Concat(CollectKeptLinkedGroups(keptIndices))
            .Concat(CollectKeptByOwnReason(keptIndices, KeepKind.NonPlacedReference, ManualPatchHintType.KeptForNonPlacedReference))
            .Concat(CollectKeptByOwnReason(keptIndices, KeepKind.TeleportDoor, ManualPatchHintType.KeptTeleportDoor))
            .OrderBy(hint => hint.Type)
            .ThenBy(hint => targets[hint.TargetIndex].Record.FormKey.ToString(), StringComparer.Ordinal)
            .ThenBy(hint => hint.Detail, StringComparer.Ordinal)
            .ToList();
    }

    private IEnumerable<ManualPatchHint> CollectRemovedMarkers(IEnumerable<Removal> removals) =>
        from removal in removals
        let kind = visibility[removal.TargetIndex].Kind
        where kind is { } markerKind && UsedMarkerKinds.Contains(markerKind)
        select new ManualPatchHint(ManualPatchHintType.RemovedMarker, removal.TargetIndex, $"{kind}");

    private IEnumerable<ManualPatchHint> CollectKeptLinkedGroups(IEnumerable<int> keptIndices) =>
        keptIndices
            .Where(groups.IsLinked)
            .Select(groups.MembersOf)
            .DistinctBy(members => members[0])
            .Select(members => new ManualPatchHint(ManualPatchHintType.KeptLinkedGroup, members[0], DescribeKeptGroup(members)));

    private string DescribeKeptGroup(IReadOnlyList<int> members)
    {
        var names = members.Select(member => RecordNames.Describe(targets[member].Record));
        var reasons = WithOwnReasons(members).Select(entry => $"{RecordNames.Describe(targets[entry.Index].Record)}: {entry.Reason.Detail}");
        return $"members {string.Join(", ", names)}; kept because {string.Join("; ", reasons)}";
    }

    private IEnumerable<ManualPatchHint> CollectKeptByOwnReason(IEnumerable<int> keptIndices, KeepKind kind, ManualPatchHintType type) =>
        WithOwnReasons(keptIndices)
            .Where(entry => entry.Reason.Kind == kind)
            .Select(entry => new ManualPatchHint(type, entry.Index, entry.Reason.Detail));

    /// <returns>The objects that must stay themselves, with their reason.</returns>
    private IEnumerable<(int Index, KeepReason Reason)> WithOwnReasons(IEnumerable<int> indices) =>
        indices
            .Select(index => (Index: index, Reason: keepRule.GetOwnReason(index)))
            .Where(entry => entry.Reason != null)
            .Select(entry => (entry.Index, entry.Reason!));
}
