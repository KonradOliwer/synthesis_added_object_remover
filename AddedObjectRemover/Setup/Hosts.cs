namespace AddedObjectRemover;

/// <summary>
/// The active rival each invisible target object sits inside; the lowest-id one when several contain it.
/// </summary>
internal sealed class Hosts
{
    private readonly OtherObject?[]? _hosts;

    private Hosts(OtherObject?[]? hosts)
    {
        _hosts = hosts;
    }

    public static Hosts NotComputed { get; } = new(null);

    /// <summary>Null for a target that is visible or sits inside no object.</summary>
    public OtherObject? HostOf(int targetIndex) =>
        (_hosts ?? throw new InvalidOperationException("The hosts were not computed in this run."))[targetIndex];

    /// <param name="visibility">Parallel to <paramref name="targets"/>; only invisible targets have a host.</param>
    public static Hosts Find(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<ObjectVisibility> visibility,
        IActiveRivals rivals,
        WorkOrder order,
        ParallelOptions options)
    {
        return new(ParallelMap.Run(
            options,
            order,
            targets.Count,
            () => new SpatialQueryScratch(),
            (i, scratch) => visibility[i].Kind != null ? FindHost(targets[i], rivals, scratch) : null));
    }

    private static OtherObject? FindHost(TargetObject target, IActiveRivals rivals, SpatialQueryScratch scratch) =>
        rivals.FirstCovering(target.SpaceKey, target.Transform.Position, scratch) is { } host ? rivals.Get(host) : null;
}
