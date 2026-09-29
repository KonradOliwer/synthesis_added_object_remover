using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>
/// The visible other-mod object each invisible target object sits inside, as far as the target
/// plugin has not replaced it; the lowest-index one when several contain it.
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
    /// <param name="indexes">Must hold every space of an invisible target.</param>
    public static Hosts Find(
        IReadOnlyList<TargetObject> targets,
        IReadOnlyList<ObjectVisibility> visibility,
        IReadOnlyDictionary<FormKey, OtherObjectIndex> indexes,
        ObjectContainment containment,
        Replacements replacements,
        ParallelOptions options)
    {
        var hosts = new OtherObject?[targets.Count];
        Parallel.For(
            0,
            targets.Count,
            options,
            () => new SpatialQueryScratch(),
            (i, _, scratch) =>
            {
                if (visibility[i].Kind != null) hosts[i] = FindHost(targets[i], indexes[targets[i].SpaceKey], containment, replacements, scratch);
                return scratch;
            },
            _ => { });
        return new Hosts(hosts);
    }

    private static OtherObject? FindHost(
        TargetObject target,
        OtherObjectIndex index,
        ObjectContainment containment,
        Replacements replacements,
        SpatialQueryScratch scratch)
    {
        var match = containment.FindContainingVisible(index, target.Transform.Position, replacements, scratch);
        return match >= 0 ? index[match] : null;
    }
}
