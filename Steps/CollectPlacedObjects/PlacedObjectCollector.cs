using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>
/// Turns the placed record facts into target objects (the target plugin's own and the other plugins' objects it changed,
/// <see cref="ChangedObjectKind"/>), other mods' objects the game shows changed from their accepted values
/// (the rule <see cref="IOtherRecordChange"/>), and support-only objects, with their roles, and joins the links into the target objects. Apart
/// from these, it collects what a restore reads (<see cref="ChangedAreaRecordCollector"/>). A removed record
/// (<see cref="RemovedForm"/>) is no object and its links are dropped. The facts are the final world, which
/// includes what earlier patchers in this group wrote into the patch. The facts come in the order the plugins were read; the
/// objects are then put in <see cref="RecordKeyOrder"/>, which gives them their ids.
/// </summary>
public sealed class PlacedObjectCollector
{
    /// <summary>Vanilla keyword LocTypeHold. Only the Location of the hold itself is sure to have it: the Major and Minor variants are not on every hold.</summary>
    private static readonly RecordKey HoldLocationKeyword = new(new PluginName("Skyrim.esm"), 0x016771);

    private readonly WhatToCollect _plan;
    private readonly PlacedRecordFacts _facts;
    private readonly ChangedAreaRecordCollector _changedAreaRecords;
    private readonly RecordClassifier _classifier;
    private readonly ReadCounters _counters;
    private readonly PlacedObjectFactory _factory;
    private readonly WorldWithoutCleanedMod _worldWithoutCleanedMod;
    private readonly CollectedObjectsAssembler _assembler;
    private readonly GatheredObjects _gathered = new();

    private PlacedObjectCollector(ModTrustTable trust, AcceptedWorld world, WhatToCollect plan, PlacedRecordFacts facts, RecordChangeRules recordChange)
    {
        _plan = plan;
        _facts = facts;
        _changedAreaRecords = new ChangedAreaRecordCollector(trust, facts);
        _classifier = new RecordClassifier(trust, world, plan, facts, recordChange.InWorld);
        _counters = new ReadCounters(trust);
        _factory = new PlacedObjectFactory(trust, plan, facts, TargetAreaNames.Of(facts.CellAreas), _changedAreaRecords);
        _worldWithoutCleanedMod = new WorldWithoutCleanedMod(trust, plan, facts, _changedAreaRecords, recordChange.InEarlierWorlds);
        _assembler = new CollectedObjectsAssembler(trust, plan, facts, _changedAreaRecords, _classifier);
    }

    /// <param name="InWorld">Decides whether another mod's placed record is changed in the accepted world the run judges in.</param>
    /// <param name="InEarlierWorlds">
    /// Decides the same in the worlds without and before the cleaned mod. It must count no part an earlier run gave back: such a part
    /// is part of the world as the patch now holds it, never of the world without or before this run's cleaned mod, whose own value
    /// it may be.
    /// </param>
    public sealed record RecordChangeRules(IOtherRecordChange InWorld, IOtherRecordChange InEarlierWorlds);

    /// <param name="World">The accepted world the other mods' records are judged in.</param>
    /// <param name="RecordChange">The rules that decide whether another mod's placed record is changed.</param>
    /// <param name="TimeSinceStart">The run's clock, which times the two parts of the step.</param>
    public sealed record Input(
        IPluginRecords Plugin, ModTrustTable Trust, AcceptedWorld World, WhatToCollect Plan, RecordChangeRules RecordChange, Func<TimeSpan> TimeSinceStart);

    public static PlacedObjectsCollected Collect(Input input)
    {
        var (scanned, scanTime) = Timed.Run(
            input.TimeSinceStart, () => ScanPlacedRecords(input.Plugin, input.Trust, input.World, input.Plan, input.RecordChange));
        var (collected, linkReadTime) = Timed.Run(input.TimeSinceStart, () => ReadNonPlacedLinks(scanned, input.Plugin));
        return new PlacedObjectsCollected(collected, scanTime, linkReadTime);
    }

    /// <summary>The first part of the step: reads the placed records and sorts them into objects; <see cref="ReadNonPlacedLinks"/> finishes it.</summary>
    /// <param name="world">The accepted world the other mods' records are judged in.</param>
    internal static ScannedPlacedRecords ScanPlacedRecords(
        IPluginRecords plugin, ModTrustTable trust, AcceptedWorld world, WhatToCollect plan, RecordChangeRules recordChange)
    {
        var facts = plugin.ReadPlacedRecords(
            new PlacedReadScope(
                trust.CleanedMod,
                Terrain: true,
                plan.Needs.Navmesh,
                plan.Needs.TerrainVersions,
                plan.Needs.TerrainLayers,
                plan.Needs.Water,
                HoldLocationKeyword,
                plan.Needs.TerrainForChangedObjectsOnly,
                ResavedPlacedParts.Tolerances,
                plan.Needs.AreaRecordVersions),
            (winner, candidate, cleanedModOverridesLand) => StopsTheWalk(trust, world, plan, winner, candidate, cleanedModOverridesLand));
        return new PlacedObjectCollector(trust, world, plan, facts, recordChange).Scan();
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
    internal static CollectedObjects ReadNonPlacedLinks(ScannedPlacedRecords scanned, IPluginRecords plugin)
    {
        var objects = scanned.Objects;
        var targetKeys = LinksIntoTargets.KeysOf(objects.Targets);
        var changedKeys = scanned.MarksLinksIntoChangedRecords
            ? objects.ChangedRecords.Select(changed => changed.Key).Concat(objects.ChangedCells.Select(changed => changed.Cell))
            : [];
        var nonPlaced = plugin.ReadLinks(
            scanned.CleanedMod,
            targetKeys.Concat(changedKeys).ToHashSet(),
            targetKeys.Select(key => key.Plugin).ToHashSet());
        var links = LinksIntoTargets.Keep(nonPlaced.Links, scanned.RemovedRecords, scanned.CleanedMod, targetKeys);
        var linked = scanned.MarksLinksIntoChangedRecords
            ? ChangedAreaRecordCollector.LinkedFromCleanedMod(nonPlaced.Links, scanned.RemovedRecords, scanned.CleanedMod)
            : [];
        return ChangedAreaRecordCollector.MarkLinked(objects with { Links = [.. objects.Links, .. links], UnreadableRecords = nonPlaced.Unreadable }, linked);
    }

    /// <remarks>
    /// A version fully trusted in the accepted world has its values accepted whatever lies below it, so no lower version can change
    /// a decision in it. When the old places are judged, the same must hold in the world without the cleaned mod, so the walk goes
    /// on below a trusted plugin that needs the cleaned mod. The cleaned mod never stops the walk: a record it overrides keeps the
    /// version it replaces, which tells whether it changed the record (<see cref="ChangedObjectKind"/>); a record it creates has
    /// no version below it. A record the patch wins is walked to its lowest version: a value only the patch holds is accepted when
    /// any trusted version below holds it (<see cref="AcceptedVersions"/>). When every object is decided in the world before the
    /// cleaned mod too, which only ground the cleaned mod changed asks for, the walk goes on below every plugin that loads after the
    /// cleaned mod, down to the versions that world believes.
    /// </remarks>
    private static bool StopsTheWalk(ModTrustTable trust, AcceptedWorld world, WhatToCollect plan, PluginName winner, PluginName plugin, bool cleanedModOverridesLand) =>
        world.TrustOf(winner) != VersionTrust.Transparent
        && plugin != trust.CleanedMod
        && world.TrustOf(plugin) == VersionTrust.Full
        && (!plan.Needs.JudgeOldPlaces || trust.AcceptedWorldWithoutCleanedMod.TrustOf(plugin) == VersionTrust.Full)
        && (!plan.Needs.DecideEveryObjectBeforeCleanedMod || !cleanedModOverridesLand || trust.AcceptedWorldBeforeCleanedMod.TrustOf(plugin) == VersionTrust.Full);

    private ScannedPlacedRecords Scan()
    {
        foreach (var record in _facts.Records) Collect(record);
        _worldWithoutCleanedMod.DecideChangesInTheWorldsAsked(_gathered);
        return _assembler.CreateScannedPlacedRecords(_gathered, _counters);
    }

    private void Collect(PlacedRecordFact record)
    {
        if (_classifier.IsCreatedByTarget(record)) _counters.CountCleanedModObject();
        CountOrNoteCloneMarker(record);
        _changedAreaRecords.Collect(record);
        if (RemovedForm.IsRemoved(record.Placement.Presence)) CountRemovedRecord(record);
        else CollectByRole(record);
    }

    /// <summary>Counts every clone an earlier run placed, whatever its final version, and notes a broken marker, which is user data.</summary>
    private void CountOrNoteCloneMarker(PlacedRecordFact record)
    {
        if (_classifier.CloneMarkOf(record) is { } clone) _counters.CountClone(clone.CleanedMod);
        else if (_classifier.HasBrokenCloneMarker(record)) _gathered.BrokenCloneMarkers.Add(record.Key);
    }

    /// <summary>The record's final version is removed in the game, so it is no object of any kind and its links keep nothing.</summary>
    private void CountRemovedRecord(PlacedRecordFact record)
    {
        _gathered.RemovedRecords.Add(record.Key);
        if (_classifier.IsCleanedModsObject(record)) _counters.CountRemovedTargetRecord(record.WinningMod);
    }

    /// <remarks>
    /// Targets define the target spaces, so no placement outside them can matter: a record there is hidden. A target record there
    /// (one an earlier patcher moved into another space) counts with the hidden ones.
    /// </remarks>
    private void CollectByRole(PlacedRecordFact record)
    {
        var classification = _classifier.Classify(record);
        var role = classification.Role;
        var presence = record.InPluginSpace ? StartsDisabledRule.Of(record.Placement, isWinningTarget: classification.IsJudged) : ShownInGame.Hidden;

        if (!classification.IsJudged && role != RecordRole.OtherChanged && presence == ShownInGame.Present)
        {
            _gathered.SupportOnlyObjects.Add(_factory.CreateOtherObject(record, change: null));
        }
        if (CountIfSkipped(role, record, presence)) return;

        if (presence != ShownInGame.Present)
        {
            if (role == RecordRole.Target) _counters.CountTargetHiddenOrWithoutPlacement();
            else if (presence == ShownInGame.InvalidPlacement) _counters.CountOtherInvalidPlacement();
            return;
        }

        if (classification.Origin is { } origin) _gathered.Targets.Add(_factory.CreateTarget(record, origin, classification.EarlierVersion, classification.CloneOf));
        else _gathered.OtherModObjects.Add(_factory.CreateOtherObject(record, classification.Change));
    }

    /// <returns>True when the record is neither a target nor another mod's object.</returns>
    private bool CountIfSkipped(RecordRole role, PlacedRecordFact record, ShownInGame presence)
    {
        switch (role)
        {
            case RecordRole.TargetOverriddenLater:
                _counters.CountTargetOverriddenLater();
                return true;
            case RecordRole.OverriddenByTarget:
                _counters.CountOtherOverriddenByTarget();
                if (_plan.Needs.OtherModDetails) _gathered.OverriddenOthers.Add(new OverriddenOtherRecord(record.Key, record.EditorId, record.WinningMod));
                return true;
            case RecordRole.OtherUnchanged:
                if (presence == ShownInGame.Present) _counters.CountOtherUnchanged();
                else if (presence == ShownInGame.InvalidPlacement) _counters.CountOtherInvalidPlacement();
                return true;
            case RecordRole.SupportOnlyCopy:
                return true;
            default:
                return false;
        }
    }
}
