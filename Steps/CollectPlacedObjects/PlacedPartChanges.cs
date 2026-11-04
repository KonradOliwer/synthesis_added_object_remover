using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>
/// What the cleaned mod last changed in a placed record of another plugin it overrides, part by part (<see cref="PartHistory"/>), over
/// the versions from the one just below the cleaned mod's up to the winner, the patch included.
/// </summary>
/// <param name="Undo">The parts whose earlier value is written back (<see cref="PartOutcome.Undo"/>).</param>
/// <param name="LeftToLaterPlugins">The parts the cleaned mod changed and a later plugin or the patch changed again, each with the last of them.</param>
/// <param name="KeptForRecordFormat">
/// The parts that would be undone, but whose earlier value holds bytes Mutagen does not read, stored in another record format than
/// the winner's, so they cannot be written back and stay.
/// </param>
internal readonly record struct PlacedPartChanges(
    PlacedParts Undo, ImmutableSortedDictionary<PlacedParts, PluginName> LeftToLaterPlugins, PlacedParts KeptForRecordFormat)
{
    /// <summary>A door's navmesh link goes back only together with its navmesh, so it is judged with the navmeshes (<see cref="NavDoorLinkOf"/>), not here.</summary>
    private static readonly ImmutableArray<PlacedParts> JudgedParts =
        [.. Enum.GetValues<PlacedParts>().Where(part => part is not PlacedParts.None and not PlacedParts.NavDoorLink)];

    /// <param name="trust">Names the patch, whose version holds what earlier patchers of the group wrote, and the plugins that need the cleaned mod.</param>
    public static PlacedPartChanges Of(PlacedPartDifferences differences, ModTrustTable trust)
    {
        var plugins = PartHistory.PluginsOf(differences.Versions);
        var undo = PlacedParts.None;
        var keptForRecordFormat = PlacedParts.None;
        var leftToLater = ImmutableSortedDictionary.CreateBuilder<PlacedParts, PluginName>();
        foreach (var part in JudgedParts)
        {
            switch (PartHistory.Of(differences.Versions, plugins, trust, parts => parts.HasFlag(part)))
            {
                case PartOutcome.Undo when !differences.LowestNotCopyable.HasFlag(part):
                    undo |= part;
                    break;
                case PartOutcome.Undo:
                    keptForRecordFormat |= part;
                    break;
                case PartOutcome.LeftToLaterPlugin later:
                    leftToLater.Add(part, later.Plugin);
                    break;
            }
        }
        return new PlacedPartChanges(undo, leftToLater.ToImmutable(), keptForRecordFormat);
    }

    /// <summary>What the cleaned mod last changed in the navmesh link of a door it overrides.</summary>
    public static PartOutcome NavDoorLinkOf(PlacedPartDifferences differences, ModTrustTable trust) =>
        PartHistory.Of(differences.Versions, PartHistory.PluginsOf(differences.Versions), trust, parts => parts.HasFlag(PlacedParts.NavDoorLink));
}
