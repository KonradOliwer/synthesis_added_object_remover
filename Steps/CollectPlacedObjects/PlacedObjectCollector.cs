using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>
/// Turns the placed record facts into target objects (the target plugin's own and the moved replacements,
/// <see cref="MovedReplacement"/>), other mods' objects the game shows changed from their accepted values
/// (<see cref="OtherRecordChange"/>), and support-only objects, with their roles, and joins the links into the target objects. A
/// removed record (<see cref="RemovedForm"/>) is none of them and its links are dropped. The facts are the final world, which
/// includes what earlier patchers in this group wrote into the patch. The facts come in the order the plugins were read; the
/// objects are then put in <see cref="RecordKeyOrder"/>, which gives them their ids.
/// </summary>
public sealed class PlacedObjectCollector
{
    private enum RecordRole { Target, TargetOverriddenLater, MovedReplacement, OverriddenByTarget, OtherUnchanged, OtherChanged }

    /// <param name="Change">Only for <see cref="RecordRole.OtherChanged"/>.</param>
    /// <param name="Replaced">Only for <see cref="RecordRole.MovedReplacement"/>.</param>
    private readonly record struct Classification(RecordRole Role, ObjectChange? Change, ReplacedVersion? Replaced)
    {
        public bool IsJudged => Role is RecordRole.Target or RecordRole.MovedReplacement;
    }

    /// <summary>Vanilla keyword LocTypeHold. Only the Location of the hold itself is sure to have it: the Major and Minor variants are not on every hold.</summary>
    private static readonly RecordKey HoldLocationKeyword = new(new PluginName("Skyrim.esm"), 0x016771);

    private readonly ModTrustTable _trust;
    private readonly WhatToCollect _plan;
    private readonly PlacedRecordFacts _facts;
    private readonly TargetAreaNames _areaNames;

    private readonly List<TargetObject> _targets = [];
    private readonly List<OtherObject> _otherModObjects = [];
    private readonly List<OtherObject> _supportOnlyObjects = [];
    private readonly List<SpaceFact> _targetSpaces = [];
    private readonly List<OverriddenOtherRecord> _overriddenOthers = [];
    private readonly List<RecordKey> _removedRecords = [];

    private int _cleanedModObjects;
    private int _targetsOverriddenLater;
    private int _targetsHiddenOrWithoutPlacement;
    private int _removedByEarlierPatchers;
    private int _removedByOtherPlugins;
    private int _othersOverriddenByTarget;
    private int _othersUnchanged;
    private int _otherInvalidPlacements;

    private PlacedObjectCollector(ModTrustTable trust, WhatToCollect plan, PlacedRecordFacts facts)
    {
        _trust = trust;
        _plan = plan;
        _facts = facts;
        _areaNames = TargetAreaNames.Of(facts.CellAreas);
    }

    public static CollectedObjects Collect(IPluginRecords plugin, ModTrustTable trust, WhatToCollect plan)
    {
        var facts = plugin.ReadPlacedRecords(
            new PlacedReadScope(trust.CleanedMod, plan.Terrain, plan.Navmesh, plan.TerrainVersions, plan.TerrainLayers, plan.Water, HoldLocationKeyword),
            candidate => StopsTheWalk(trust, candidate));
        var collector = new PlacedObjectCollector(trust, plan, facts);
        foreach (var record in facts.Records) collector.Collect(record);

        var targetKeys = collector._targets.Select(target => target.Key).ToHashSet();
        var nonPlaced = plugin.ReadLinks(trust.CleanedMod, targetKeys);
        var links = collector.KeepLinksIntoTargets([.. facts.Links, .. nonPlaced.Links], targetKeys);
        return collector.CreateCollectedObjects(links, nonPlaced.Unreadable);
    }

    /// <remarks>
    /// A fully trusted version's values are accepted whatever lies below it, so no lower version can change a decision. The cleaned
    /// mod is fully trusted but does not stop the walk: a record it overrides keeps the version it replaces, which tells whether it
    /// moved the record (<see cref="MovedReplacement"/>); a record it creates has no version below it.
    /// </remarks>
    private static bool StopsTheWalk(ModTrustTable trust, PluginName plugin) =>
        plugin != trust.CleanedMod && trust.VersionTrustOf(plugin) == VersionTrust.Full;

    private void Collect(PlacedRecordFact record)
    {
        if (IsCreatedByTarget(record)) _cleanedModObjects++;
        if (RemovedForm.IsRemoved(record.Placement.Presence)) CountRemovedRecord(record);
        else CollectByRole(record);
    }

    /// <summary>The record's final version is removed in the game, so it is no object of any kind and its links keep nothing.</summary>
    private void CountRemovedRecord(PlacedRecordFact record)
    {
        _removedRecords.Add(record.Key);
        if (IsCreatedByTarget(record)) CountRemovedTargetRecord(record.WinningMod);
    }

    /// <param name="winner">The plugin whose version removes the record; the target's own removal counts with its hidden records.</param>
    private void CountRemovedTargetRecord(PluginName winner)
    {
        if (winner == _trust.Patch) _removedByEarlierPatchers++;
        else if (winner == _trust.CleanedMod) _targetsHiddenOrWithoutPlacement++;
        else _removedByOtherPlugins++;
    }

    /// <summary>
    /// The links into the target plugin and into the moved replacements, without those from removed records. The reader also gives
    /// the links into the records the target overrides and leaves where they were, which judge nothing.
    /// </summary>
    private ImmutableArray<LinkFact> KeepLinksIntoTargets(ImmutableArray<LinkFact> links, IReadOnlySet<RecordKey> targetKeys)
    {
        var removed = _removedRecords.ToHashSet();
        return [.. links.Where(link => !removed.Contains(link.Source) && (link.Target.Plugin == _trust.CleanedMod || targetKeys.Contains(link.Target)))];
    }

    /// <remarks>
    /// Targets define the target spaces, so no placement outside them can matter: a record there is hidden. A target record there
    /// (one an earlier patcher moved into another space) counts with the hidden ones.
    /// </remarks>
    private void CollectByRole(PlacedRecordFact record)
    {
        var classification = Classify(record);
        var role = classification.Role;
        var presence = record.InTargetSpace ? StartsDisabledRule.Of(record.Placement, isWinningTarget: classification.IsJudged) : ShownInGame.Hidden;

        if (_plan.SupportOnlyObjects && !classification.IsJudged && role != RecordRole.OtherChanged && presence == ShownInGame.Present)
        {
            _supportOnlyObjects.Add(CreateOtherObject(record, editorId: null, change: null));
        }
        if (CountIfSkipped(role, record, presence)) return;

        if (presence != ShownInGame.Present)
        {
            if (role == RecordRole.Target) _targetsHiddenOrWithoutPlacement++;
            else if (presence == ShownInGame.InvalidPlacement) _otherInvalidPlacements++;
            return;
        }

        if (classification.IsJudged) AddTarget(record, classification.Replaced);
        else _otherModObjects.Add(CreateOtherObject(record, _plan.OtherModDetails ? record.EditorId : null, classification.Change));
    }

    /// <returns>True when the record is neither a target nor another mod's object.</returns>
    private bool CountIfSkipped(RecordRole role, PlacedRecordFact record, ShownInGame presence)
    {
        switch (role)
        {
            case RecordRole.TargetOverriddenLater:
                _targetsOverriddenLater++;
                return true;
            case RecordRole.OverriddenByTarget:
                _othersOverriddenByTarget++;
                if (_plan.OtherModDetails) _overriddenOthers.Add(new OverriddenOtherRecord(record.Key, record.EditorId, record.WinningMod));
                return true;
            case RecordRole.OtherUnchanged:
                if (presence == ShownInGame.Present) _othersUnchanged++;
                else if (presence == ShownInGame.InvalidPlacement) _otherInvalidPlacements++;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Creator first: the target's own records, then the records the target overrides, then the rule decides once.</summary>
    private Classification Classify(PlacedRecordFact record)
    {
        if (IsCreatedByTarget(record)) return new(WinsInTargetOrPatch(record) ? RecordRole.Target : RecordRole.TargetOverriddenLater, null, null);
        if (_facts.OverriddenRecords.Contains(record.Key))
        {
            var replaced = MovedReplacement.ReplacedVersionOf(record, _trust.CleanedMod, _trust.Patch, _plan.MovedDistance);
            return new(replaced is null ? RecordRole.OverriddenByTarget : RecordRole.MovedReplacement, null, replaced);
        }
        var change = OtherRecordChange.Decide(record, _trust);
        return new(change is null ? RecordRole.OtherUnchanged : RecordRole.OtherChanged, change, null);
    }

    private bool IsCreatedByTarget(PlacedRecordFact record) => record.Key.Plugin == _trust.CleanedMod;

    /// <remarks>The patch's version holds what earlier patchers in this group wrote, so a target record it wins is judged as the patch left it.</remarks>
    private bool WinsInTargetOrPatch(PlacedRecordFact record) => record.WinningMod == _trust.CleanedMod || record.WinningMod == _trust.Patch;

    /// <param name="replaced">The version a moved replacement replaces; null for the target's own records.</param>
    private void AddTarget(PlacedRecordFact record, ReplacedVersion? replaced)
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
            HasEnableParent: record.Placement.Presence.EnableParent is not null,
            NamedByScript: record.NamedByScript,
            HasLocationRefType: record.HasLocationRefType,
            ReferenceRadius: record.ReferenceRadius,
            PrimitiveBounds: record.PrimitiveBounds,
            Area: _areaNames.Name(record.Space, record.Cell.Key, position),
            Replaced: replaced));
    }

    private static OtherObject CreateOtherObject(PlacedRecordFact record, string? editorId, ObjectChange? change)
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
            record.HasMapMarker,
            change);
    }

    /// <remarks>Only for a record whose presence is <see cref="ShownInGame.Present"/>, which has a position and a rotation.</remarks>
    private static (Vector3 Position, Vector3 Rotation) PlacementOf(PlacedRecordFact record) =>
        (record.Placement.Position!.Value, record.Placement.EulerRotation!.Value);

    private CollectedObjects CreateCollectedObjects(ImmutableArray<LinkFact> links, ImmutableArray<UnreadableRecord> unreadable)
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
            unreadable,
            _targetSpaces.DistinctBy(space => space.Key).ToDictionary(space => space.Key),
            new ReadCounts(
                _facts.RecordsScanned,
                _cleanedModObjects,
                _targetsOverriddenLater,
                _targetsHiddenOrWithoutPlacement,
                _removedByEarlierPatchers,
                _removedByOtherPlugins,
                _othersOverriddenByTarget,
                _othersUnchanged,
                _otherInvalidPlacements,
                _facts.NavmeshCount),
            [.. _overriddenOthers],
            TargetWorkOrder.Of(targets),
            targets.IsEmpty ? new StopReason(StopKind.NoTargetObjects, null) : null);
    }

    private static ImmutableArray<OtherObject> NumberInKeyOrder(IEnumerable<OtherObject> others, int firstId) =>
        DenseNumbering.InOrder(
            others, other => other.Key, RecordKeyOrder.Comparer, firstId, (other, id) => other with { Id = new OtherId(id) });
}
