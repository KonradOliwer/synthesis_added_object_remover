using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>
/// Turns the placed record facts into target objects (the target plugin's own and the other plugins' objects it changed,
/// <see cref="ChangedObjectKind"/>), other mods' objects the game shows changed from their accepted values
/// (<see cref="OtherRecordChange"/>), and support-only objects, with their roles, and joins the links into the target objects. Apart
/// from these, it lists every record of another plugin whose parts the cleaned mod changed (<see cref="RecordChangedByCleanedMod"/>),
/// and with <see cref="WhatToCollect.AreaRecordVersions"/> every such cell and LAND record (<see cref="CellChangedByCleanedMod"/>,
/// <see cref="LandChangedByCleanedMod"/>). A removed record (<see cref="RemovedForm"/>) is no object and its links are dropped. The facts are the final world, which
/// includes what earlier patchers in this group wrote into the patch. The facts come in the order the plugins were read; the
/// objects are then put in <see cref="RecordKeyOrder"/>, which gives them their ids.
/// </summary>
public sealed class PlacedObjectCollector
{
    private enum RecordRole { Target, TargetOverriddenLater, Moved, Replaced, OverriddenByTarget, OtherUnchanged, OtherChanged }

    /// <param name="Change">Only for <see cref="RecordRole.OtherChanged"/>.</param>
    /// <param name="EarlierVersion">Only for <see cref="RecordRole.Moved"/> and <see cref="RecordRole.Replaced"/>.</param>
    private readonly record struct Classification(RecordRole Role, ObjectChange? Change, PlacedVersionFact? EarlierVersion)
    {
        /// <summary>Null for a record that is not a target object.</summary>
        public TargetOrigin? Origin => Role switch
        {
            RecordRole.Target => TargetOrigin.CleanedMod,
            RecordRole.Moved => TargetOrigin.Moved,
            RecordRole.Replaced => TargetOrigin.Replaced,
            RecordRole.TargetOverriddenLater or RecordRole.OverriddenByTarget or RecordRole.OtherUnchanged or RecordRole.OtherChanged => null,
            _ => throw new UnreachableException($"Unknown record role {Role}."),
        };

        public bool IsJudged => Origin is not null;
    }

    /// <summary>Vanilla keyword LocTypeHold. Only the Location of the hold itself is sure to have it: the Major and Minor variants are not on every hold.</summary>
    private static readonly RecordKey HoldLocationKeyword = new(new PluginName("Skyrim.esm"), 0x016771);

    private readonly ModTrustTable _trust;
    private readonly AcceptedWorld _world;
    private readonly WhatToCollect _plan;
    private readonly PlacedRecordFacts _facts;
    private readonly TargetAreaNames _areaNames;
    private readonly AreaRecordChanges _areaRecordChanges;

    private readonly List<TargetObject> _targets = [];
    private readonly List<OtherObject> _otherModObjects = [];
    private readonly List<OtherObject> _supportOnlyObjects = [];
    private readonly List<OverriddenOtherRecord> _overriddenOthers = [];
    private readonly List<RecordKey> _removedRecords = [];
    private readonly List<RecordChangedByCleanedMod> _changedRecords = [];

    private int _cleanedModObjects;
    private int _targetsOverriddenLater;
    private int _targetsHiddenOrWithoutPlacement;
    private int _removedByEarlierPatchers;
    private int _removedByOtherPlugins;
    private int _othersOverriddenByTarget;
    private int _othersUnchanged;
    private int _otherInvalidPlacements;

    private PlacedObjectCollector(ModTrustTable trust, AcceptedWorld world, WhatToCollect plan, PlacedRecordFacts facts)
    {
        _trust = trust;
        _world = world;
        _plan = plan;
        _facts = facts;
        _areaNames = TargetAreaNames.Of(facts.CellAreas);
        _areaRecordChanges = new AreaRecordChanges(
            trust.Patch, facts.Records.Where(record => record.PartDifferences is not null).ToDictionary(record => record.Key, record => record.PartDifferences!));
    }

    /// <summary>The first part of the step: reads the placed records and sorts them into objects; <see cref="ReadNonPlacedLinks"/> finishes it.</summary>
    /// <param name="world">The accepted world the other mods' records are judged in.</param>
    public static ScannedPlacedRecords ScanPlacedRecords(IPluginRecords plugin, ModTrustTable trust, AcceptedWorld world, WhatToCollect plan)
    {
        var facts = plugin.ReadPlacedRecords(
            new PlacedReadScope(
                trust.CleanedMod,
                Terrain: true,
                plan.Navmesh,
                plan.TerrainVersions,
                plan.TerrainLayers,
                plan.Water,
                HoldLocationKeyword,
                plan.TerrainForChangedObjectsOnly,
                OtherRecordChange.Tolerances,
                plan.AreaRecordVersions),
            candidate => StopsTheWalk(trust, world, plan, candidate));
        var collector = new PlacedObjectCollector(trust, world, plan, facts);
        foreach (var record in facts.Records) collector.Collect(record);
        collector.DecideChangesWithoutCleanedModIfOldPlacesAreJudged();
        return collector.CreateScannedPlacedRecords();
    }

    /// <summary>
    /// Reads the links from records other than placed objects into the scanned targets, the changed records and the changed cells,
    /// adds those into the targets after the placed records' links, and marks the changed records and cells the cleaned mod's
    /// records link to.
    /// </summary>
    /// <remarks>
    /// Only the plugins that can link to a target are read: the changed records and cells count only links from the cleaned mod's
    /// records, which lie in plugins that can link to the cleaned mod, so they add no plugin to read.
    /// </remarks>
    public static CollectedObjects ReadNonPlacedLinks(ScannedPlacedRecords scanned, IPluginRecords plugin)
    {
        var objects = scanned.Objects;
        var targetKeys = TargetKeys(objects.Targets);
        var changedKeys = scanned.MarksLinksIntoChangedRecords
            ? objects.ChangedRecords.Select(changed => changed.Key).Concat(objects.ChangedCells.Select(changed => changed.Cell))
            : [];
        var nonPlaced = plugin.ReadLinks(
            scanned.CleanedMod,
            targetKeys.Concat(changedKeys).ToHashSet(),
            targetKeys.Select(key => key.Plugin).ToHashSet());
        var links = KeepLinksIntoTargets(nonPlaced.Links, scanned.RemovedRecords, scanned.CleanedMod, targetKeys);
        var linked = scanned.MarksLinksIntoChangedRecords
            ? LinkedFromCleanedMod(nonPlaced.Links, scanned.RemovedRecords, scanned.CleanedMod)
            : [];
        return MarkLinked(objects with { Links = [.. objects.Links, .. links], UnreadableRecords = nonPlaced.Unreadable }, linked);
    }

    private ScannedPlacedRecords CreateScannedPlacedRecords()
    {
        var removed = _removedRecords.ToImmutableHashSet();
        var placedLinks = KeepLinksIntoTargets(_facts.Links, removed, _trust.CleanedMod, TargetKeys(_targets));
        return new ScannedPlacedRecords(CreateCollectedObjects(placedLinks, removed), _trust.CleanedMod, removed, _plan.AreaRecordVersions is not null);
    }

    private static HashSet<RecordKey> TargetKeys(IEnumerable<TargetObject> targets) => targets.Select(target => target.Key).ToHashSet();

    /// <remarks>
    /// A version fully trusted in the accepted world has its values accepted whatever lies below it, so no lower version can change
    /// a decision in it. When the old places are judged, the same must hold in the world without the cleaned mod, so the walk goes
    /// on below a trusted plugin that needs the cleaned mod. The cleaned mod never stops the walk: a record it overrides keeps the
    /// version it replaces, which tells whether it changed the record (<see cref="ChangedObjectKind"/>); a record it creates has
    /// no version below it.
    /// </remarks>
    private static bool StopsTheWalk(ModTrustTable trust, AcceptedWorld world, WhatToCollect plan, PluginName plugin) =>
        plugin != trust.CleanedMod
        && world.TrustOf(plugin) == VersionTrust.Full
        && (!plan.JudgeOldPlaces || trust.AcceptedWorldWithoutCleanedMod.TrustOf(plugin) == VersionTrust.Full);

    private void Collect(PlacedRecordFact record)
    {
        if (IsCreatedByTarget(record)) _cleanedModObjects++;
        if (CreateChangedRecord(record) is { } changed) _changedRecords.Add(changed);
        if (RemovedForm.IsRemoved(record.Placement.Presence)) CountRemovedRecord(record);
        else CollectByRole(record);
    }

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
    /// The links into the target plugin and into the changed objects, without those from removed records. The reader also gives
    /// the links into the records the target overrides and leaves where they were, which judge nothing.
    /// </summary>
    private static ImmutableArray<LinkFact> KeepLinksIntoTargets(
        IEnumerable<LinkFact> links, IReadOnlySet<RecordKey> removed, PluginName cleanedMod, IReadOnlySet<RecordKey> targetKeys) =>
        [.. links.Where(link => !removed.Contains(link.Source) && (link.Target.Plugin == cleanedMod || targetKeys.Contains(link.Target)))];

    /// <summary>
    /// The records that a record the cleaned mod creates, not removed in the game, links to. The links into the changed records and
    /// cells are not kept with the target links: those count the links from the targets.
    /// </summary>
    private static HashSet<RecordKey> LinkedFromCleanedMod(IEnumerable<LinkFact> links, IReadOnlySet<RecordKey> removed, PluginName cleanedMod) =>
        links.Where(link => link.Source.Plugin == cleanedMod && !removed.Contains(link.Source)).Select(link => link.Target).ToHashSet();

    /// <summary>Marks each changed record and cell that is in <paramref name="linked"/>.</summary>
    private static CollectedObjects MarkLinked(CollectedObjects objects, IReadOnlySet<RecordKey> linked) => objects with
    {
        ChangedRecords = [.. objects.ChangedRecords.Select(record => linked.Contains(record.Key) ? record with { LinkedFromCleanedMod = true } : record)],
        ChangedCells = [.. objects.ChangedCells.Select(cell => linked.Contains(cell.Cell) ? cell with { LinkedFromCleanedMod = true } : cell)],
    };

    /// <remarks>
    /// Targets define the target spaces, so no placement outside them can matter: a record there is hidden. A target record there
    /// (one an earlier patcher moved into another space) counts with the hidden ones.
    /// </remarks>
    private void CollectByRole(PlacedRecordFact record)
    {
        var classification = Classify(record);
        var role = classification.Role;
        var presence = record.InTargetSpace ? StartsDisabledRule.Of(record.Placement, isWinningTarget: classification.IsJudged) : ShownInGame.Hidden;

        if (!classification.IsJudged && role != RecordRole.OtherChanged && presence == ShownInGame.Present)
        {
            _supportOnlyObjects.Add(CreateOtherObject(record, _plan.OtherModDetails ? record.EditorId : null, change: null));
        }
        if (CountIfSkipped(role, record, presence)) return;

        if (presence != ShownInGame.Present)
        {
            if (role == RecordRole.Target) _targetsHiddenOrWithoutPlacement++;
            else if (presence == ShownInGame.InvalidPlacement) _otherInvalidPlacements++;
            return;
        }

        if (classification.Origin is { } origin) AddTarget(record, origin, classification.EarlierVersion);
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
        if (_facts.OverriddenRecords.Contains(record.Key)) return ClassifyOverriddenRecord(record);
        var change = OtherRecordChange.Decide(record, _world);
        return new(change is null ? RecordRole.OtherUnchanged : RecordRole.OtherChanged, change, null);
    }

    private Classification ClassifyOverriddenRecord(PlacedRecordFact record)
    {
        var changed = ChangedObjectKind.Of(record, _trust.CleanedMod, _world, _plan.MovedDistance);
        return changed switch
        {
            { Origin: TargetOrigin.Moved } => new(RecordRole.Moved, null, changed.Value.EarlierVersion),
            { Origin: TargetOrigin.Replaced } when _plan.ProcessReplaced => new(RecordRole.Replaced, null, changed.Value.EarlierVersion),
            _ => new(RecordRole.OverriddenByTarget, null, null),
        };
    }

    private bool IsCreatedByTarget(PlacedRecordFact record) => record.Key.Plugin == _trust.CleanedMod;

    /// <remarks>The patch's version holds what earlier patchers in this group wrote, so a target record it wins is judged as the patch left it.</remarks>
    private bool WinsInTargetOrPatch(PlacedRecordFact record) => record.WinningMod == _trust.CleanedMod || record.WinningMod == _trust.Patch;

    private void AddTarget(PlacedRecordFact record, TargetOrigin origin, PlacedVersionFact? earlierVersion)
    {
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
            CollisionLayer: record.Winner.Reference.CollisionLayer,
            IsCullingVolume: record.Winner.Reference.IsCullingVolume,
            Area: _areaNames.Name(record.Space, record.Cell.Key, position),
            Origin: origin,
            EarlierVersion: earlierVersion is { } earlier ? CreateEarlierVersion(record, earlier) : null)
        {
            CellDeleted = DeletedInFinalWorld(record.Cell),
        });
    }

    /// <param name="earlier">The version just below the cleaned mod's, with a position and a rotation.</param>
    /// <remarks>A record that a version above the cleaned mod's removes was taken out after it, so none of its parts goes back.</remarks>
    private EarlierVersion CreateEarlierVersion(PlacedRecordFact record, PlacedVersionFact earlier)
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

    /// <summary>The cell a restore writes the record into: the earlier version's when the restore moves it there, else the one it stands in.</summary>
    private static CellFact CellRestoredInto(PlacedRecordFact record, EarlierVersion earlier) =>
        earlier.GoesBackToEarlierList || earlier.GoesBackToEarlierCell(record.Cell, record.IsPersistent) ? earlier.Cell : record.Cell;

    private DeletedCell? DeletedInFinalWorld(CellFact cell) =>
        _facts.CellsDeletedInFinalWorld.TryGetValue(cell.Key, out var plugin) ? new DeletedCell(cell, plugin) : null;

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

    private OtherObject CreateOtherObject(PlacedRecordFact record, string? editorId, ObjectChange? change)
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
            change,
            RolesOf(record),
            ChangeWithoutCleanedMod: null)
        {
            CellDeleted = DeletedInFinalWorld(record.Cell),
        };
    }

    /// <remarks>
    /// Only a changed object has an old place to judge, so a run without one decides nothing in the world without the cleaned mod,
    /// unless the plan asks for every object and some placed record of another plugin the cleaned mod changed can go back.
    /// </remarks>
    private void DecideChangesWithoutCleanedModIfOldPlacesAreJudged()
    {
        if (!_plan.JudgeOldPlaces) return;
        var everyObject = _plan.DecideEveryObjectWithoutCleanedMod && _changedRecords.Count > 0;
        if (!everyObject && _targets.All(target => target.Origin == TargetOrigin.CleanedMod)) return;

        var recordOf = _facts.Records.ToDictionary(record => record.Key);
        DecideChangesWithoutCleanedMod(_otherModObjects, recordOf);
        DecideChangesWithoutCleanedMod(_supportOnlyObjects, recordOf);
    }

    private void DecideChangesWithoutCleanedMod(List<OtherObject> objects, IReadOnlyDictionary<RecordKey, PlacedRecordFact> recordOf)
    {
        for (var index = 0; index < objects.Count; index++)
        {
            var change = OtherRecordChange.Decide(recordOf[objects[index].Key], _trust.AcceptedWorldWithoutCleanedMod);
            objects[index] = objects[index] with { ChangeWithoutCleanedMod = change };
        }
    }

    private ReferenceRoles RolesOf(PlacedRecordFact record) =>
        (record.IsPersistent ? ReferenceRoles.Persistent : ReferenceRoles.None)
        | (record.IsTeleportDoor ? ReferenceRoles.TeleportDoor : ReferenceRoles.None)
        | (record.Placement.Presence.EnableParent is not null ? ReferenceRoles.HasEnableParent : ReferenceRoles.None)
        | (record.NamedByScript is not null ? ReferenceRoles.ScriptNamesQuestOrScene : ReferenceRoles.None)
        | (record.HasLocationRefType ? ReferenceRoles.LocationRefType : ReferenceRoles.None)
        | (_facts.LinkedFromPlacedRecords.Contains(record.Key) ? ReferenceRoles.LinkedFromPlacedRecord : ReferenceRoles.None)
        | (record.HasMapMarker ? ReferenceRoles.MapMarker : ReferenceRoles.None);

    /// <remarks>Only for a record whose presence is <see cref="ShownInGame.Present"/>, which has a position and a rotation.</remarks>
    private static (Vector3 Position, Vector3 Rotation) PlacementOf(PlacedRecordFact record) =>
        (record.Placement.Position!.Value, record.Placement.EulerRotation!.Value);

    /// <summary>The objects with the placed records' links; the step's second part adds the other links and the unreadable records.</summary>
    private CollectedObjects CreateCollectedObjects(ImmutableArray<LinkFact> placedLinks, IReadOnlySet<RecordKey> removed)
    {
        var targets = _targets
            .OrderBy(target => target.Key, RecordKeyOrder.Comparer)
            .Select((target, index) => target with { Id = new TargetId(index) })
            .ToImmutableArray();
        var linked = _plan.AreaRecordVersions is not null ? LinkedFromCleanedMod(_facts.Links, removed, _trust.CleanedMod) : [];
        var objects = new CollectedObjects(
            targets,
            NumberInKeyOrder(_otherModObjects, firstId: 0),
            NumberInKeyOrder(_supportOnlyObjects, firstId: _otherModObjects.Count),
            OrderChangedRecords(targets),
            ChangedCellsInKeyOrder(),
            ChangedLandsInKeyOrder(),
            ChangedNavmeshesInKeyOrder(),
            placedLinks,
            [],
            _facts.Spaces,
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
            targets.IsEmpty ? new StopReason(StopKind.NoTargetObjects, null) : null)
        {
            WinningPresence = WinningPresenceOfTargetSpaces(),
            WinningPrimitives = WinningPrimitivesOfTargetSpaces(),
        };
        return MarkLinked(objects, linked);
    }

    private Dictionary<RecordKey, ReferenceFacts> WinningPrimitivesOfTargetSpaces() =>
        _facts.Records.Where(record => record is { InTargetSpace: true, IsPrimitive: true }).ToDictionary(record => record.Key, record => record.Winner.Reference);

    private Dictionary<RecordKey, PlacedPresence> WinningPresenceOfTargetSpaces() =>
        _facts.Records
            .Where(record => record.InTargetSpace)
            .Select(record => KeyValuePair.Create(record.Key, WinningPresenceOf(record)))
            .Where(entry => entry.Value != default)
            .ToDictionary();

    /// <remarks>Only a record that is Initially Disabled needs its role, which is costly to decide, to tell whether the flag hides it.</remarks>
    private PlacedPresence WinningPresenceOf(PlacedRecordFact record) =>
        RemovedForm.PresenceOf(record.Placement.Presence, isWinningTarget: record.Placement.Presence.InitiallyDisabled && Classify(record).IsJudged);

    /// <summary>The changed records in key order, each with the id of the target object it is judged as.</summary>
    private ImmutableArray<RecordChangedByCleanedMod> OrderChangedRecords(ImmutableArray<TargetObject> targets)
    {
        var targetIds = targets.ToDictionary(target => target.Key, target => target.Id);
        return InKeyOrder(_changedRecords, changed => changed.Key, changed => targetIds.TryGetValue(changed.Key, out var id) ? changed with { Target = id } : changed);
    }

    private ImmutableArray<CellChangedByCleanedMod> ChangedCellsInKeyOrder() => InKeyOrder(_facts.ChangedCells, cell => cell.Key, _areaRecordChanges.CellOf);

    private ImmutableArray<LandChangedByCleanedMod> ChangedLandsInKeyOrder() => InKeyOrder(_facts.ChangedLands, land => land.Key, _areaRecordChanges.LandOf);

    private ImmutableArray<NavmeshChangedByCleanedMod> ChangedNavmeshesInKeyOrder() => InKeyOrder(_facts.ChangedNavmeshes, navmesh => navmesh.Key, _areaRecordChanges.NavmeshOf);

    /// <param name="toChanged">Null for a record with no change to list.</param>
    private static ImmutableArray<TChanged> InKeyOrder<TFact, TChanged>(IEnumerable<TFact> facts, Func<TFact, RecordKey> keyOf, Func<TFact, TChanged?> toChanged)
        where TChanged : class =>
        [.. facts.OrderBy(keyOf, RecordKeyOrder.Comparer).Select(toChanged).OfType<TChanged>()];

    private static ImmutableArray<OtherObject> NumberInKeyOrder(IEnumerable<OtherObject> others, int firstId) =>
        DenseNumbering.InOrder(
            others, other => other.Key, RecordKeyOrder.Comparer, firstId, (other, id) => other with { Id = new OtherId(id) });
}
