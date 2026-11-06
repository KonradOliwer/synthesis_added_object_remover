using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>
/// One version of a record set against the others read with it: its plugin and which of its parts differ. The versions are read
/// lowest first, from the one just below a plugin the caller names up to the winner, so the named plugin's version is the second.
/// </summary>
/// <param name="FromBelow">The parts whose value differs from the version just below; none for the lowest version.</param>
/// <param name="FromNamedPlugin">The parts whose value differs from the named plugin's version; none for that version and the one below it.</param>
/// <param name="FromLowest">The parts whose value differs from the lowest version, the one just below the named plugin's; none for that version.</param>
public readonly record struct VersionDifferences<TParts>(PluginName Plugin, TParts FromBelow, TParts FromNamedPlugin, TParts FromLowest);

/// <summary>How one part of a record differs across its versions (<see cref="VersionDifferences{TParts}"/>).</summary>
/// <param name="LowestNotCopyable">
/// The lowest version's value cannot be written into the winner: it holds bytes Mutagen does not read, stored in another record
/// format than the winner's.
/// </param>
public sealed record PartDifferences(ImmutableArray<VersionDifferences<bool>> Versions, bool LowestNotCopyable);

/// <summary>Compares the versions of a record read from the one just below a named plugin up to the winner.</summary>
public static class VersionComparison
{
    /// <summary>The index of the named plugin's version: the versions start just below it, so its own is the second.</summary>
    public const int NamedPluginIndex = 1;

    /// <param name="plugins">The plugin of each version, lowest first.</param>
    /// <param name="differing">The parts whose values differ between two versions, given the lower index first.</param>
    /// <param name="none">No part.</param>
    public static ImmutableArray<VersionDifferences<TParts>> Compare<TParts>(IReadOnlyList<PluginName> plugins, Func<int, int, TParts> differing, TParts none) =>
    [
        .. plugins.Select((plugin, index) =>
        {
            var fromBelow = index == 0 ? none : differing(index - 1, index);
            return new VersionDifferences<TParts>(
                plugin,
                fromBelow,
                index <= NamedPluginIndex ? none : differing(NamedPluginIndex, index),
                index <= NamedPluginIndex ? fromBelow : differing(0, index));
        }),
    ];

    /// <param name="differs">Whether the part's values differ between two versions, given the lower index first.</param>
    public static PartDifferences ComparePart(IReadOnlyList<PluginName> plugins, Func<int, int, bool> differs, bool lowestNotCopyable) =>
        new(Compare(plugins, differs, none: false), lowestNotCopyable);

    /// <summary>
    /// Whether a part differs between two versions, given the lower index first. Only neighbouring versions, and the named plugin's
    /// version and the lowest version with one above them, are compared.
    /// </summary>
    /// <param name="includes">Whether a set of parts includes the part.</param>
    public static Func<int, int, bool> Differs<TParts>(ImmutableArray<VersionDifferences<TParts>> versions, Func<TParts, bool> includes) =>
        (lower, higher) =>
            higher == lower + 1 ? includes(versions[higher].FromBelow)
            : lower == NamedPluginIndex && higher > lower ? includes(versions[higher].FromNamedPlugin)
            : lower == 0 && higher > lower ? includes(versions[higher].FromLowest)
            : throw new ArgumentException($"Versions {lower} and {higher} were not compared; only neighbours, and the named plugin's version and the lowest version with one above them, are.");

    /// <summary>The named plugin's version: the second, since the versions start just below it.</summary>
    public static TVersion NamedPluginVersionOf<TVersion>(IReadOnlyList<TVersion> versions) => versions[NamedPluginIndex];

    /// <summary>The versions above the named plugin's, lowest first.</summary>
    public static IEnumerable<TVersion> AboveNamedPlugin<TVersion>(IReadOnlyList<TVersion> versions) => versions.Skip(NamedPluginIndex + 1);
}
