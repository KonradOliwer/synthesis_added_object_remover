namespace AddedObjectRemover;

/// <summary>The rivals the too-close step leaves out as a whole.</summary>
/// <param name="InvisibleByReason">Invisible rivals of the spaces holding a visible target, most frequent reason first, ties by reason.</param>
/// <param name="PlacedNpcs">Placed NPCs among the rivals of the target spaces.</param>
internal sealed record RivalCensus(IReadOnlyList<KeyValuePair<string, int>> InvisibleByReason, int PlacedNpcs);
