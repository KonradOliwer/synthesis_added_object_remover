using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>The restored parts a run knows when it starts, by their record. Thread-safe.</summary>
public sealed class RestoredPartsByRecord(ImmutableArray<RestoredPart> parts)
{
    private readonly ILookup<RecordKey, RestoredPart> _byRecord = parts.ToLookup(static part => part.Record);

    /// <returns>In the order known; empty where none of the record's parts was given back.</returns>
    public IEnumerable<RestoredPart> Of(RecordKey record) => _byRecord[record];

    /// <summary>The plugins whose values the record's part was given back, each once.</summary>
    public static PluginName[] SourcesOf(IEnumerable<RestoredPart> restored, RestoredPartName part) =>
        [.. restored.Where(known => known.Part == part).Select(static known => known.Source).Distinct()];
}
