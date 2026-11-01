namespace AddedObjectRemover;

/// <summary>How one part of a placed record is compared between two versions and whether one version's value can be written into another.</summary>
/// <param name="Differs">Whether the values of two versions differ, the lower version first.</param>
/// <param name="AllowsCopy">Whether the first version's value can be written into the second version.</param>
public sealed record PlacedPartCompare<TVersion>(PlacedParts Part, Func<TVersion, TVersion, bool> Differs, Func<TVersion, TVersion, bool> AllowsCopy);

/// <summary>Sets the versions of a placed record against each other, part by part (<see cref="VersionDifferences{TParts}"/>).</summary>
public static class PlacedPartComparison
{
    /// <param name="versions">Lowest first: the version just below the named plugin's, the named plugin's, then each later one up to the winner.</param>
    /// <param name="plugins">The plugin of each version.</param>
    public static PlacedPartDifferences Compare<TVersion>(IReadOnlyList<PlacedPartCompare<TVersion>> parts, IReadOnlyList<TVersion> versions, IReadOnlyList<PluginName> plugins) =>
        new(
            VersionComparison.Compare(plugins, (lower, higher) => DifferingParts(parts, versions[lower], versions[higher]), PlacedParts.None),
            parts.Where(part => !part.AllowsCopy(versions[0], versions[^1])).Aggregate(PlacedParts.None, (notCopyable, part) => notCopyable | part.Part));

    private static PlacedParts DifferingParts<TVersion>(IEnumerable<PlacedPartCompare<TVersion>> parts, TVersion lower, TVersion higher) =>
        parts.Where(part => part.Differs(lower, higher)).Aggregate(PlacedParts.None, (differing, part) => differing | part.Part);
}
