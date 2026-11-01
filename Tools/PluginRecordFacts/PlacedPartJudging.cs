using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>How one part of a placed record is compared between two versions and whether one version's value can be written into another.</summary>
/// <param name="Differs">Whether the values of two versions differ, the lower version first.</param>
/// <param name="AllowsCopy">Whether the first version's value can be written into the second version.</param>
public sealed record PlacedPartCompare<TVersion>(PlacedParts Part, Func<TVersion, TVersion, bool> Differs, Func<TVersion, TVersion, bool> AllowsCopy);

/// <summary>What the target plugin last changed in a placed record of another plugin it overrides, part by part (<see cref="PartHistory"/>).</summary>
public static class PlacedPartJudging
{
    /// <param name="versions">Lowest first: the version just below the target plugin's, the target plugin's, then each later one up to the winner.</param>
    /// <param name="plugins">The plugin of each version.</param>
    /// <param name="patch">The patch, whose version holds what earlier patchers of the group wrote.</param>
    public static PlacedPartChanges Judge<TVersion>(
        IEnumerable<PlacedPartCompare<TVersion>> parts, IReadOnlyList<TVersion> versions, IReadOnlyList<PluginName> plugins, PluginName patch)
    {
        var undo = PlacedParts.None;
        var keptForRecordFormat = PlacedParts.None;
        var leftToLater = ImmutableSortedDictionary.CreateBuilder<PlacedParts, PluginName>();
        foreach (var part in parts)
        {
            switch (PartHistory.Of(plugins, PartHistory.TargetVersionIndex, patch, (lower, higher) => part.Differs(versions[lower], versions[higher])))
            {
                case PartOutcome.Undo when part.AllowsCopy(versions[0], versions[^1]):
                    undo |= part.Part;
                    break;
                case PartOutcome.Undo:
                    keptForRecordFormat |= part.Part;
                    break;
                case PartOutcome.LeftToLaterPlugin later:
                    leftToLater.Add(part.Part, later.Plugin);
                    break;
            }
        }
        return new PlacedPartChanges(undo, leftToLater.ToImmutable(), keptForRecordFormat);
    }
}
