using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>Puts the gathered objects, the counts and the changed area records into the scan's result, giving the objects their ids in <see cref="RecordKeyOrder"/>.</summary>
internal sealed class CollectedObjectsAssembler(
    ModTrustTable trust, WhatToCollect plan, PlacedRecordFacts facts, ChangedAreaRecordCollector changedAreaRecords, RecordClassifier classifier)
{
    public ScannedPlacedRecords CreateScannedPlacedRecords(GatheredObjects gathered, ReadCounters counters)
    {
        var removed = gathered.RemovedRecords.ToImmutableHashSet();
        var placedLinks = LinksIntoTargets.Keep(facts.Links, removed, trust.CleanedMod, LinksIntoTargets.KeysOf(gathered.Targets));
        return new ScannedPlacedRecords(CreateCollectedObjects(gathered, counters, placedLinks, removed), trust.CleanedMod, removed, ReadsAreaRecords);
    }

    private bool ReadsAreaRecords => plan.Needs.AreaRecordVersions is not null;

    /// <summary>The objects with the placed records' links; the step's second part adds the other links and the unreadable records.</summary>
    private CollectedObjects CreateCollectedObjects(GatheredObjects gathered, ReadCounters counters, ImmutableArray<LinkFact> placedLinks, IReadOnlySet<RecordKey> removed)
    {
        var targets = gathered.Targets
            .OrderBy(target => target.Key, RecordKeyOrder.Comparer)
            .Select((target, index) => target with { Id = new TargetId(index) })
            .ToImmutableArray();
        var linked = ReadsAreaRecords ?ChangedAreaRecordCollector.LinkedFromCleanedMod(facts.Links, removed, trust.CleanedMod) : [];
        var objects = new CollectedObjects(
            targets,
            NumberInKeyOrder(gathered.OtherModObjects, firstId: 0),
            NumberInKeyOrder(gathered.SupportOnlyObjects, firstId: gathered.OtherModObjects.Count),
            changedAreaRecords.OrderChangedRecords(targets),
            changedAreaRecords.ChangedCellsInKeyOrder(),
            changedAreaRecords.ChangedLandsInKeyOrder(),
            changedAreaRecords.ChangedNavmeshesInKeyOrder(),
            placedLinks,
            [],
            facts.Spaces,
            counters.ToReadCounts(facts),
            [.. gathered.OverriddenOthers],
            TargetWorkOrder.Of(targets),
            targets.IsEmpty ? new StopReason(StopKind.NoTargetObjects, null) : null)
        {
            WinningPresence = WinningPresenceOfTargetSpaces(),
            WinningPrimitives = WinningPrimitivesOfTargetSpaces(),
            BrokenCloneMarkers = [.. gathered.BrokenCloneMarkers],
            CleanedModsObjectsOverriddenLater = [.. gathered.CleanedModsObjectsOverriddenLater.OrderBy(overridden => overridden.Key, RecordKeyOrder.Comparer)],
        };
        return ChangedAreaRecordCollector.MarkLinked(objects, linked);
    }

    private Dictionary<RecordKey, ReferenceFacts> WinningPrimitivesOfTargetSpaces() =>
        facts.Records.Where(record => record is { InPluginSpace: true, IsPrimitive: true }).ToDictionary(record => record.Key, record => record.Winner.Reference);

    private Dictionary<RecordKey, PlacedPresence> WinningPresenceOfTargetSpaces() =>
        facts.Records
            .Where(record => record.InPluginSpace)
            .Select(record => KeyValuePair.Create(record.Key, WinningPresenceOf(record)))
            .Where(entry => entry.Value != default)
            .ToDictionary();

    /// <remarks>Only a record that is Initially Disabled needs its role, which is costly to decide, to tell whether the flag hides it.</remarks>
    private PlacedPresence WinningPresenceOf(PlacedRecordFact record) =>
        RemovedForm.PresenceOf(record.Placement.Presence, isWinningTarget: record.Placement.Presence.InitiallyDisabled && classifier.Classify(record).IsJudged);

    private static ImmutableArray<OtherObject> NumberInKeyOrder(IEnumerable<OtherObject> others, int firstId) =>
        DenseNumbering.InOrder(
            others, other => other.Key, RecordKeyOrder.Comparer, firstId, (other, id) => other with { Id = new OtherId(id) });
}
