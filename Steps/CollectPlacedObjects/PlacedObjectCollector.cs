using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.WriteThePatch.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>
/// Turns the placed record facts into target objects and other mods' objects, with their roles,
/// and joins the links into the target plugin. The facts come in the order the plugins were read; the
/// objects are then put in <see cref="RecordKeyOrder"/>, which gives them their ids.
/// </summary>
public sealed class PlacedObjectCollector
{
    private enum RecordRole { Target, TargetOverriddenLater, IgnoredOrigin, OverriddenByTarget, Other }

    /// <summary>Vanilla keyword LocTypeHold. Only the Location of the hold itself is sure to have it: the Major and Minor variants are not on every hold.</summary>
    private static readonly RecordKey HoldLocationKeyword = new(new PluginName("Skyrim.esm"), 0x016771);

    private readonly IdentifiedMods _standing;
    private readonly WhatToCollect _plan;
    private readonly PlacedRecordFacts _facts;
    private readonly WrittenByEarlierRuns _earlier;
    private readonly TargetAreaNames _areaNames;

    private readonly List<TargetObject> _targets = [];
    private readonly List<OtherObject> _otherModObjects = [];
    private readonly List<OtherObject> _supportOnlyObjects = [];
    private readonly List<SpaceFact> _targetSpaces = [];
    private readonly List<OverriddenOtherRecord> _overriddenOthers = [];

    private int _targetsOverriddenLater;
    private int _targetsHiddenOrWithoutPlacement;
    private int _othersOverriddenByTarget;
    private int _otherInvalidPlacements;
    private int _removedByEarlierRuns;
    private int _movedByEarlierRuns;

    private PlacedObjectCollector(IdentifiedMods standing, WhatToCollect plan, PlacedRecordFacts facts, WrittenByEarlierRuns earlier)
    {
        _standing = standing;
        _plan = plan;
        _facts = facts;
        _earlier = earlier;
        _areaNames = TargetAreaNames.Of(facts.CellAreas);
    }

    /// <param name="earlier">
    /// Applied here, before any record is judged, so every later step sees the earlier runs' writes. It wins over what
    /// the load order reads back from the patch: a removed record with an Enable Parent reads back as present.
    /// </param>
    public static CollectedObjects Collect(IPluginRecords plugin, IdentifiedMods standing, WhatToCollect plan, WrittenByEarlierRuns earlier)
    {
        var facts = plugin.ReadPlacedRecords(new PlacedReadScope(standing.Target, plan.Terrain, plan.Navmesh, plan.TerrainVersions, plan.TerrainLayers, HoldLocationKeyword));
        var collector = new PlacedObjectCollector(standing, plan, facts, earlier);
        foreach (var record in facts.Records) collector.Collect(record);

        var nonPlacedLinks = plugin.ReadLinks(standing.Target, collector._targets.Select(target => target.Key).ToHashSet());
        return collector.CreateCollectedObjects(collector.WithoutLinksFromRemovedRecords([.. facts.Links, .. nonPlacedLinks]));
    }

    private void Collect(PlacedRecordFact record)
    {
        if (CountIfRemovedByEarlierRuns(record)) return;
        CollectByRole(AtPositionOfEarlierRuns(record));
    }

    /// <returns>True when an earlier run removed the record, so it is no object of any kind in this run.</returns>
    private bool CountIfRemovedByEarlierRuns(PlacedRecordFact record)
    {
        if (!_earlier.Disabled.Contains(record.Key)) return false;
        if (record.InTargetSpace) _removedByEarlierRuns++;
        return true;
    }

    private PlacedRecordFact AtPositionOfEarlierRuns(PlacedRecordFact record)
    {
        if (!_earlier.MovedTo.TryGetValue(record.Key, out var position)) return record;
        if (record.InTargetSpace) _movedByEarlierRuns++;
        return record with { Placement = record.Placement with { Position = position } };
    }

    private ImmutableArray<LinkFact> WithoutLinksFromRemovedRecords(ImmutableArray<LinkFact> links) =>
        [.. links.Where(link => !_earlier.Disabled.Contains(link.Source))];

    private void CollectByRole(PlacedRecordFact record)
    {
        var role = Classify(record);
        // Targets define the target spaces, so no placement outside them can matter.
        var presence = record.InTargetSpace ? StartsDisabledRule.Of(record.Placement, isWinningTarget: role == RecordRole.Target) : ShownInGame.Hidden;

        if (_plan.SupportOnlyObjects && role is not (RecordRole.Target or RecordRole.Other) && presence == ShownInGame.Present)
        {
            _supportOnlyObjects.Add(CreateOtherObject(record, editorId: null));
        }
        if (CountIfSkipped(role, record) || !record.InTargetSpace) return;

        if (presence != ShownInGame.Present)
        {
            if (role == RecordRole.Target) _targetsHiddenOrWithoutPlacement++;
            else if (presence == ShownInGame.InvalidPlacement) _otherInvalidPlacements++;
            return;
        }

        if (role == RecordRole.Target) AddTarget(record);
        else _otherModObjects.Add(CreateOtherObject(record, _plan.OtherModDetails ? record.EditorId : null));
    }

    /// <returns>True when the record is neither a target nor another mod's object.</returns>
    private bool CountIfSkipped(RecordRole role, PlacedRecordFact record)
    {
        switch (role)
        {
            case RecordRole.TargetOverriddenLater:
                _targetsOverriddenLater++;
                return true;
            case RecordRole.IgnoredOrigin:
                return true;
            case RecordRole.OverriddenByTarget:
                _othersOverriddenByTarget++;
                if (_plan.OtherModDetails) _overriddenOthers.Add(new OverriddenOtherRecord(record.Key, record.EditorId, record.WinningMod));
                return true;
            default:
                return false;
        }
    }

    private RecordRole Classify(PlacedRecordFact record)
    {
        var origin = record.Key.Plugin;
        if (origin == _standing.Target)
        {
            // Also true when an earlier patcher in this run (the patch mod) overrode it.
            return record.WinningMod == _standing.Target ? RecordRole.Target : RecordRole.TargetOverriddenLater;
        }
        if (_standing.IgnoredOrigins.Contains(origin)) return RecordRole.IgnoredOrigin;
        return _facts.OverriddenRecords.Contains(record.Key) ? RecordRole.OverriddenByTarget : RecordRole.Other;
    }

    private void AddTarget(PlacedRecordFact record)
    {
        _targetSpaces.Add(record.Space);

        var (position, rotation) = PlacementOf(record);
        _targets.Add(new TargetObject(
            Id: default,
            Key: record.Key,
            EditorId: record.EditorId,
            SpaceKey: record.Space.Key,
            Cell: record.Cell.Key != record.Space.Key ? record.Cell : null,
            Transform: new PlacedTransform(position, Mat3.FromEuler(rotation), ReferenceScale.Normalize(record.Scale)),
            Rotation: rotation,
            Base: record.Base,
            IsTeleportDoor: record.IsTeleportDoor,
            IsPrimitive: record.IsPrimitive,
            HasMapMarker: record.HasMapMarker,
            IsPersistent: record.IsPersistent,
            NamedByScript: record.NamedByScript,
            HasLocationRefType: record.HasLocationRefType,
            ReferenceRadius: record.ReferenceRadius,
            PrimitiveBounds: record.PrimitiveBounds,
            Area: _areaNames.Name(record.Space, record.Cell.Key, position)));
    }

    private static OtherObject CreateOtherObject(PlacedRecordFact record, string? editorId)
    {
        var (position, rotation) = PlacementOf(record);
        return new OtherObject(
            default,
            record.Key,
            record.Space.Key,
            record.WinningMod,
            editorId,
            record.Base,
            position,
            rotation,
            ReferenceScale.Normalize(record.Scale),
            record.IsPrimitive,
            record.HasMapMarker);
    }

    /// <remarks>Only for a record whose presence is <see cref="ShownInGame.Present"/>, which has a position and a rotation.</remarks>
    private static (Vector3 Position, Vector3 Rotation) PlacementOf(PlacedRecordFact record) =>
        (record.Placement.Position!.Value, record.Placement.EulerRotation!.Value);

    private CollectedObjects CreateCollectedObjects(ImmutableArray<LinkFact> links)
    {
        var targets = _targets
            .OrderBy(target => target.Key, RecordKeyOrder.Comparer)
            .Select((target, index) => target with { Id = new TargetId(index) })
            .ToImmutableArray();
        return new CollectedObjects(
            targets,
            NumberInKeyOrder(_otherModObjects, firstId: 0),
            _plan.SupportOnlyObjects ? NumberInKeyOrder(_supportOnlyObjects, firstId: _otherModObjects.Count) : ImmutableArray<OtherObject>.Empty,
            _plan.SupportOnlyObjects,
            links,
            _targetSpaces.DistinctBy(space => space.Key).ToDictionary(space => space.Key),
            new ReadCounts(
                _facts.RecordsScanned,
                _targetsOverriddenLater,
                _targetsHiddenOrWithoutPlacement,
                _othersOverriddenByTarget,
                _otherInvalidPlacements,
                _facts.NavmeshCount,
                _removedByEarlierRuns,
                _movedByEarlierRuns),
            [.. _overriddenOthers],
            TargetWorkOrder.Of(targets),
            targets.IsEmpty ? new StopReason(StopKind.NoTargetObjects, null) : null);
    }

    private static ImmutableArray<OtherObject> NumberInKeyOrder(IEnumerable<OtherObject> others, int firstId) =>
        DenseNumbering.InOrder(
            others, other => other.Key, RecordKeyOrder.Comparer, firstId, (other, id) => other with { Id = new OtherId(id) });
}
