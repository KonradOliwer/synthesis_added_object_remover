namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>The other-mod objects the too-close step leaves out as a whole.</summary>
/// <param name="InvisibleByReason">Invisible other-mod objects of the spaces holding a visible target, most frequent reason first, ties by reason.</param>
/// <param name="PlacedNpcs">Placed NPCs among the other-mod objects of the target spaces.</param>
public sealed record InvisibleOtherObjectCounts(IReadOnlyList<KeyValuePair<string, int>> InvisibleByReason, int PlacedNpcs);
