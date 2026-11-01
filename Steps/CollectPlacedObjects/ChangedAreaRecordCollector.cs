using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>
/// What a restore reads besides the objects: every placed record of another plugin whose parts the cleaned mod changed
/// (<see cref="RecordChangedByCleanedMod"/>), and with <see cref="WhatToCollect.AreaRecordVersions"/> every such cell, LAND and
/// navmesh record (<see cref="CellChangedByCleanedMod"/>, <see cref="LandChangedByCleanedMod"/>, <see cref="NavmeshChangedByCleanedMod"/>);
/// the earlier versions of the records the cleaned mod overrides, the cells deleted in the final world, and which changed records
/// and cells the cleaned mod's records link to.
/// </summary>
internal sealed class ChangedAreaRecordCollector
{
    private readonly ModTrustTable _trust;
    private readonly PlacedRecordFacts _facts;
    private readonly AreaRecordChanges _areaRecordChanges;
    private readonly List<RecordChangedByCleanedMod> _changedRecords = [];

    public ChangedAreaRecordCollector(ModTrustTable trust, PlacedRecordFacts facts)
    {
        _trust = trust;
        _facts = facts;
        _areaRecordChanges = new AreaRecordChanges(
            trust.Patch, facts.Records.Where(record => record.PartDifferences is not null).ToDictionary(record => record.Key, record => record.PartDifferences!));
    }

    /// <summary>Whether some placed record of another plugin the cleaned mod changed was collected.</summary>
    public bool HasChangedRecords => _changedRecords.Count > 0;

    /// <summary>Adds the record when the cleaned mod changed a part of it that goes back, was changed again or stays for its record format.</summary>
    public void Collect(PlacedRecordFact record)
    {
        if (CreateChangedRecord(record) is { } changed) _changedRecords.Add(changed);
    }

    /// <summary>
    /// The records that a record the cleaned mod creates, not removed in the game, links to. The links into the changed records and
    /// cells are not kept with the target links: those count the links from the targets.
    /// </summary>
    public static HashSet<RecordKey> LinkedFromCleanedMod(IEnumerable<LinkFact> links, IReadOnlySet<RecordKey> removed, PluginName cleanedMod) =>
        links.Where(link => link.Source.Plugin == cleanedMod && !removed.Contains(link.Source)).Select(link => link.Target).ToHashSet();

    /// <summary>Marks each changed record and cell that is in <paramref name="linked"/>.</summary>
    public static CollectedObjects MarkLinked(CollectedObjects objects, IReadOnlySet<RecordKey> linked) => objects with
    {
        ChangedRecords = [.. objects.ChangedRecords.Select(record => linked.Contains(record.Key) ? record with { LinkedFromCleanedMod = true } : record)],
        ChangedCells = [.. objects.ChangedCells.Select(cell => linked.Contains(cell.Cell) ? cell with { LinkedFromCleanedMod = true } : cell)],
    };

    /// <param name="earlier">The version just below the cleaned mod's, with a position and a rotation.</param>
    /// <remarks>A record that a version above the cleaned mod's removes was taken out after it, so none of its parts goes back.</remarks>
    public EarlierVersion CreateEarlierVersion(PlacedRecordFact record, PlacedVersionFact earlier)
    {
        var differences = record.PartDifferences
            ?? throw new UnreachableException($"The reader compared no parts of the record {record.Key}, which the cleaned mod overrides.");
        var changes = PlacedPartChanges.Of(differences, _trust.Patch);
        var removedLater = record.Versions
            .Skip(ChangedObjectKind.CleanedModIndexOf(record, _trust.CleanedMod) + 1)
            .Any(version => RemovedForm.IsRemoved(version.Placement.Presence));
        var version = new EarlierVersion(
            earlier.Plugin,
            new EulerPlacement(earlier.Placement.Position!.Value, earlier.Placement.EulerRotation!.Value, ReferenceScale.Normalize(earlier.Scale)),
            earlier.Base,
            earlier.Cell,
            earlier.IsPersistent,
            removedLater ? PlacedParts.None : changes.Undo,
            changes.LeftToLaterPlugins,
            changes.KeptForRecordFormat,
            PersistenceGoesBack(record),
            earlier.Reference,
            DeletedCellRestoredInto: null);
        return version with { DeletedCellRestoredInto = DeletedInFinalWorld(CellRestoredInto(record, version)) };
    }

    public DeletedCell? DeletedInFinalWorld(CellFact cell) =>
        _facts.CellsDeletedInFinalWorld.TryGetValue(cell.Key, out var plugin) ? new DeletedCell(cell, plugin) : null;

    /// <summary>The changed records in key order, each with the id of the target object it is judged as.</summary>
    public ImmutableArray<RecordChangedByCleanedMod> OrderChangedRecords(ImmutableArray<TargetObject> targets)
    {
        var targetIds = targets.ToDictionary(target => target.Key, target => target.Id);
        return InKeyOrder(_changedRecords, changed => changed.Key, changed => targetIds.TryGetValue(changed.Key, out var id) ? changed with { Target = id } : changed);
    }

    public ImmutableArray<CellChangedByCleanedMod> ChangedCellsInKeyOrder() => InKeyOrder(_facts.ChangedCells, cell => cell.Key, _areaRecordChanges.CellOf);

    public ImmutableArray<LandChangedByCleanedMod> ChangedLandsInKeyOrder() => InKeyOrder(_facts.ChangedLands, land => land.Key, _areaRecordChanges.LandOf);

    public ImmutableArray<NavmeshChangedByCleanedMod> ChangedNavmeshesInKeyOrder() => InKeyOrder(_facts.ChangedNavmeshes, navmesh => navmesh.Key, _areaRecordChanges.NavmeshOf);

    /// <remarks>
    /// An earlier version in another space has no old place in the record's space, as for a changed object (<see cref="ChangedObjectKind"/>).
    /// A deleted earlier version is never written back: a deleted placed reference in a plugin crashes the game.
    /// </remarks>
    /// <returns>
    /// Null when the cleaned mod does not override the record, its earlier version has no old place there or is deleted, or no part
    /// goes back, was changed again or stays for its record format.
    /// </returns>
    private RecordChangedByCleanedMod? CreateChangedRecord(PlacedRecordFact record)
    {
        if (record.PartDifferences is null) return null;
        var earlier = record.Versions[ChangedObjectKind.CleanedModIndexOf(record, _trust.CleanedMod) - 1];
        if (earlier.SpaceKey != record.Space.Key || earlier.Placement is not { Position: not null, EulerRotation: not null, Presence.Deleted: false }) return null;

        var earlierVersion = CreateEarlierVersion(record, earlier);
        if (earlierVersion is { Undo: PlacedParts.None, LeftToLaterPlugins.IsEmpty: true, KeptForRecordFormat: PlacedParts.None }) return null;
        return new RecordChangedByCleanedMod(
            record.Key,
            record.EditorId,
            record.Space.Key,
            record.Cell,
            record.WinningMod,
            record.Placement is { Position: { } position, EulerRotation: { } rotation } ? new EulerPlacement(position, rotation, ReferenceScale.Normalize(record.Scale)) : null,
            record.Base,
            record.Winner.Reference,
            record.IsPersistent,
            earlierVersion,
            Target: null,
            LinkedFromCleanedMod: false,
            RemovedForm.PresenceOf(earlier.Placement.Presence, isWinningTarget: false),
            _facts.OverriddenDoorLinks.TryGetValue(record.Key, out var door) ? _areaRecordChanges.DoorOf(door) : null,
            IsInDeletedCell(record, earlier));
    }

    /// <summary>A version read deletes the cell holding the record's winning version or the one holding the given version.</summary>
    private bool IsInDeletedCell(PlacedRecordFact record, PlacedVersionFact earlier) =>
        _facts.DeletedCells.Contains(record.Cell.Key) || _facts.DeletedCells.Contains(earlier.Cell.Key);

    /// <summary>The cell a restore writes the record into: the earlier version's when the restore moves it there, else the one it stands in.</summary>
    private static CellFact CellRestoredInto(PlacedRecordFact record, EarlierVersion earlier) =>
        earlier.GoesBackToEarlierList || earlier.GoesBackToEarlierCell(record.Cell, record.IsPersistent) ? earlier.Cell : record.Cell;

    /// <summary>
    /// The Persistent flag is no part, since it follows the child list, but a later plugin may set it for its own use, such as
    /// filling a quest alias; so it is judged like a part, from the version facts.
    /// </summary>
    private bool PersistenceGoesBack(PlacedRecordFact record)
    {
        var versions = record.Versions;
        var outcome = PartHistory.Of(
            [.. versions.Select(version => version.Plugin)],
            ChangedObjectKind.CleanedModIndexOf(record, _trust.CleanedMod),
            _trust.Patch,
            (lower, higher) => versions[lower].IsPersistent != versions[higher].IsPersistent);
        return outcome is PartOutcome.Undo;
    }

    /// <param name="toChanged">Null for a record with no change to list.</param>
    private static ImmutableArray<TChanged> InKeyOrder<TFact, TChanged>(IEnumerable<TFact> facts, Func<TFact, RecordKey> keyOf, Func<TFact, TChanged?> toChanged)
        where TChanged : class =>
        [.. facts.OrderBy(keyOf, RecordKeyOrder.Comparer).Select(toChanged).OfType<TChanged>()];
}
