using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>The placed records sorted into objects: the first part of the collect step, before the links from records other than placed objects are read.</summary>
/// <param name="Objects">The objects with the placed records' links only and no unreadable record.</param>
/// <param name="CleanedMod">The plugin whose objects are judged; the links into it are kept.</param>
/// <param name="RemovedRecords">The records whose final version is removed; a link from one keeps nothing.</param>
/// <param name="MarksLinksIntoChangedRecords">Whether the links into the changed records are read and mark which of them the cleaned mod's records link to.</param>
public sealed record ScannedPlacedRecords(
    CollectedObjects Objects, PluginName CleanedMod, ImmutableHashSet<RecordKey> RemovedRecords, bool MarksLinksIntoChangedRecords);
