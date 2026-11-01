namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>What to do with a part of a record the cleaned mod may have changed.</summary>
public abstract record PartOutcome
{
    /// <summary>The cleaned mod is the last to change the part, or the patch is and holds the cleaned mod's value.</summary>
    public sealed record Undo : PartOutcome;

    /// <summary>The cleaned mod changed the part, but a later plugin or the patch changed it again; names the last of them.</summary>
    public sealed record LeftToLaterPlugin(PluginName Plugin) : PartOutcome;

    /// <summary>The cleaned mod did not change the part; a value it only carries over from below is not a change.</summary>
    public sealed record Unchanged : PartOutcome;

    /// <summary>
    /// The part would be undone, but its earlier value holds bytes Mutagen does not read, stored in another record format than the
    /// winner's, so it cannot be written back and stays.
    /// </summary>
    public sealed record KeptForRecordFormat : PartOutcome;
}
