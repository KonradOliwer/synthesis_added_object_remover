namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>What the step collected, with how long each of its two parts took.</summary>
/// <param name="ScanTime">Reading the placed records and sorting them into objects.</param>
/// <param name="LinkReadTime">Reading the links from the records other than placed objects.</param>
public sealed record PlacedObjectsCollected(CollectedObjects Objects, TimeSpan ScanTime, TimeSpan LinkReadTime);
