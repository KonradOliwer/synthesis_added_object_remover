using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>
/// Turns the placed record facts into target objects and other mods' objects, with their roles,
/// and joins the links into the target plugin. The facts come in the order the plugins were read; the
/// objects are then put in <see cref="RecordKeyOrder"/>, which gives them their ids.
/// </summary>
internal sealed class PlacedObjectCollector
{
    private enum RecordRole { Target, TargetOverriddenLater, IgnoredOrigin, OverriddenByTarget, Other }

    private readonly IdentifiedMods _standing;
    private readonly WhatToCollect _plan;
    private readonly PlacedRecordFacts _facts;

    private readonly List<TargetObject> _targets = [];
    private readonly List<OtherObject> _otherModObjects = [];
    private readonly List<OtherObject> _supportOnlyObjects = [];
    private readonly List<SpaceFact> _targetSpaces = [];
    private readonly List<OverriddenOtherRecord> _overriddenOthers = [];

    private int _targetsOverriddenLater;
    private int _targetsHiddenOrWithoutPlacement;
    private int _othersOverriddenByTarget;
    private int _otherInvalidPlacements;

    private PlacedObjectCollector(IdentifiedMods standing, WhatToCollect plan, PlacedRecordFacts facts)
    {
        _standing = standing;
        _plan = plan;
        _facts = facts;
    }

    public static CollectedObjects Collect(IPluginRecords plugin, IdentifiedMods standing, WhatToCollect plan)
    {
        var facts = plugin.ReadPlacedRecords(new PlacedReadScope(standing.Target, plan.Terrain, plan.Navmesh));
        var collector = new PlacedObjectCollector(standing, plan, facts);
        foreach (var record in facts.Records) collector.Collect(record);

        var nonPlacedLinks = plugin.ReadLinks(standing.Target, collector._targets.Select(target => target.Key).ToHashSet());
        return collector.CreateCollectedObjects([.. facts.Links, .. nonPlacedLinks]);
    }

    private void Collect(PlacedRecordFact record)
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
        else _otherModObjects.Add(CreateOtherObject(record, _plan.OtherEditorIds ? record.EditorId : null));
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
                if (_plan.OverriddenOthersList) _overriddenOthers.Add(new OverriddenOtherRecord(record.Key, record.EditorId, record.WinningMod));
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
            ReferenceRadius: record.ReferenceRadius,
            PrimitiveBounds: record.PrimitiveBounds));
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
        var targets = _targets.OrderBy(target => target.Key, RecordKeyOrder.Comparer).ToList();
        return new CollectedObjects(
            targets.Select((target, index) => target with { Id = new TargetId(index) }).ToImmutableArray(),
            NumberInKeyOrder(_otherModObjects, firstId: 0),
            _plan.SupportOnlyObjects ? NumberInKeyOrder(_supportOnlyObjects, firstId: _otherModObjects.Count) : null,
            links,
            _targetSpaces.DistinctBy(space => space.Key).ToDictionary(space => space.Key),
            new ReadCounts(
                _facts.RecordsScanned,
                _targetsOverriddenLater,
                _targetsHiddenOrWithoutPlacement,
                _othersOverriddenByTarget,
                _otherInvalidPlacements,
                _facts.NavmeshCount),
            [.. _overriddenOthers]);
    }

    private static ImmutableArray<OtherObject> NumberInKeyOrder(IEnumerable<OtherObject> others, int firstId) =>
        DenseNumbering.InOrder(
            others, other => other.Key, RecordKeyOrder.Comparer, firstId, (other, id) => other with { Id = new OtherId(id) });
}
