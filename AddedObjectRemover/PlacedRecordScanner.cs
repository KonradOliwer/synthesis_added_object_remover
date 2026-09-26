using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace AddedObjectRemover;

/// <summary>An other-mod record skipped because the target plugin overrides it; listed in the verbose log.</summary>
internal readonly record struct OverriddenOtherRecord(FormKey FormKey, string? EditorId, ModKey WinningMod);

internal sealed class ScanResult
{
    public List<TargetObject> Targets { get; } = [];

    /// <summary>Parallel to <see cref="Targets"/>.</summary>
    public List<TargetLocation> TargetLocations { get; } = [];

    public Dictionary<FormKey, List<OtherObject>> OthersBySpace { get; } = new();
    public Dictionary<FormKey, string> SpaceNames { get; } = new();

    /// <summary>Target FormKey -> why another placed object depends on it (first reason found).</summary>
    public Dictionary<FormKey, string> TargetReferences { get; } = new();

    public List<OverriddenOtherRecord> OverriddenOthersLog { get; } = [];

    public int RecordsScanned { get; set; }
    public int TargetsOverriddenLater { get; set; }
    public int TargetsDisabledOrWithoutPlacement { get; set; }
    public int OthersOverriddenByTarget { get; set; }

    public int OtherObjectCount => OthersBySpace.Values.Sum(x => x.Count);
}

/// <summary>
/// The spaces (worldspace or interior cell FormKeys) that contain a record created by the target
/// plugin, and every placed FormKey from another plugin that the target plugin overrides.
/// </summary>
internal sealed record TargetPluginFootprint(HashSet<FormKey> SpaceKeys, HashSet<FormKey> OverriddenFormKeys);

/// <summary>
/// Collects target objects and other mods' objects from every winning placed record (REFR, ACHR
/// and all placed trap/hazard/projectile types).
///
/// Walks each mod's cell tree once via <c>EnumerateMajorRecordContexts&lt;ICell&gt;</c> (enumerating
/// IPlaced walks each tree three times) and reads each cell's Persistent and Temporary lists itself.
/// Mods are visited highest priority first, so the first sighting of a FormKey (record or cell)
/// is its winner.
/// </summary>
internal sealed class PlacedRecordScanner
{
    private enum RecordRole { Target, TargetOverriddenLater, IgnoredOrigin, OverriddenByTarget, Other }

    private readonly IPatcherState<ISkyrimMod, ISkyrimModGetter> _state;
    private readonly RunConfig _config;
    private readonly TargetPluginFootprint _footprint;
    private readonly ScanResult _scan = new();
    private readonly HashSet<FormKey> _seenRecords = new();
    private readonly Dictionary<FormKey, IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter>> _winningCells = new();

    private PlacedRecordScanner(
        IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
        RunConfig config,
        TargetPluginFootprint footprint)
    {
        _state = state;
        _config = config;
        _footprint = footprint;
    }

    public static ScanResult Scan(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, RunConfig config)
    {
        var footprint = AnalyzeTargetPlugin(state, config);
        return new PlacedRecordScanner(state, config, footprint).ScanLoadOrder();
    }

    private ScanResult ScanLoadOrder()
    {
        foreach (var listing in _state.LoadOrder.PriorityOrder)
        {
            if (listing.Mod is not { } mod) continue;
            var collectReferences = _config.KeepReferencedObjects && MayReferenceTarget(mod);
            foreach (var cellContext in mod.EnumerateMajorRecordContexts<ICell, ICellGetter>(_state.LinkCache))
            {
                ScanCell(cellContext, listing.ModKey, collectReferences);
            }
        }
        return _scan;
    }

    /// <summary>A plugin can only link to a target FormKey if it is the target, has it as a master, or is the patch.</summary>
    private bool MayReferenceTarget(ISkyrimModGetter mod) =>
        mod.ModKey == _config.Target
        || mod.ModKey == _state.PatchMod.ModKey
        || mod.MasterReferences.Any(master => master.Master == _config.Target);

    /// <summary>
    /// Walks only the target plugin's own cell tree. A FormKey the target overrides is a
    /// replacement, never an "other mod" object, whichever plugin wins it.
    /// </summary>
    private static TargetPluginFootprint AnalyzeTargetPlugin(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, RunConfig config)
    {
        var footprint = new TargetPluginFootprint([], []);
        if (config.TargetMod is not { } mod) return footprint;

        foreach (var cellContext in mod.EnumerateMajorRecordContexts<ICell, ICellGetter>(state.LinkCache))
        {
            var hasTargetRecord = false;
            foreach (var (record, _) in cellContext.Record.EnumeratePlaced())
            {
                if (record.FormKey.ModKey == config.Target) hasTargetRecord = true;
                else footprint.OverriddenFormKeys.Add(record.FormKey);
            }
            if (hasTargetRecord) footprint.SpaceKeys.Add(GetCellSpace(cellContext).SpaceKey);
        }
        return footprint;
    }

    private void ScanCell(
        IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> cellContext,
        ModKey winningMod,
        bool collectReferences)
    {
        var cell = cellContext.Record;
        var space = GetCellSpace(cellContext);
        var inTargetSpace = _footprint.SpaceKeys.Contains(space.SpaceKey);
        if (inTargetSpace) _winningCells.TryAdd(cell.FormKey, cellContext);

        foreach (var (record, persistent) in cell.EnumeratePlaced())
        {
            ScanRecord(record, persistent, cell, space, inTargetSpace, winningMod, collectReferences);
        }
    }

    private void ScanRecord(
        IPlacedGetter record,
        bool persistent,
        ICellGetter cell,
        CellSpace space,
        bool inTargetSpace,
        ModKey winningMod,
        bool collectReferences)
    {
        if (!_seenRecords.Add(record.FormKey)) return;
        _scan.RecordsScanned++;
        if (record.IsDeleted) return;

        if (collectReferences)
        {
            TargetReferenceCollector.Collect(record, _config.Target, _scan.TargetReferences);
        }

        var role = Classify(record, winningMod);
        switch (role)
        {
            case RecordRole.TargetOverriddenLater:
                _scan.TargetsOverriddenLater++;
                return;
            case RecordRole.IgnoredOrigin:
                return;
            case RecordRole.OverriddenByTarget:
                _scan.OthersOverriddenByTarget++;
                if (_config.Verbose) _scan.OverriddenOthersLog.Add(new OverriddenOtherRecord(record.FormKey, record.EditorID, winningMod));
                return;
        }

        if (record.IsInitiallyDisabled()
            || record.Placement is not { } placement
            || !Geometry.IsWithinLimits(Geometry.ToVector(placement.Position)))
        {
            if (role == RecordRole.Target) _scan.TargetsDisabledOrWithoutPlacement++;
            return;
        }

        // Targets define the target spaces, so no object outside them can matter.
        if (!inTargetSpace) return;

        if (role == RecordRole.Target) AddTarget(record, persistent, placement, cell, space);
        else AddOther(record, placement, space.SpaceKey, winningMod);
    }

    private RecordRole Classify(IPlacedGetter record, ModKey winningMod)
    {
        var origin = record.FormKey.ModKey;
        if (origin == _config.Target)
        {
            // Also true when an earlier patcher in this run (the patch mod) overrode it.
            return winningMod == _config.Target ? RecordRole.Target : RecordRole.TargetOverriddenLater;
        }
        if (_config.IgnoredOrigins.Contains(origin)) return RecordRole.IgnoredOrigin;
        return _footprint.OverriddenFormKeys.Contains(record.FormKey) ? RecordRole.OverriddenByTarget : RecordRole.Other;
    }

    private void AddTarget(IPlacedGetter record, bool persistent, IPlacementGetter placement, ICellGetter cell, CellSpace space)
    {
        if (!_scan.SpaceNames.ContainsKey(space.SpaceKey))
        {
            _scan.SpaceNames[space.SpaceKey] = RecordNames.DescribeSpace(space.SpaceRecord);
        }

        _scan.Targets.Add(new TargetObject(
            Record: record,
            SpaceKey: space.SpaceKey,
            CellName: _config.Verbose && cell.FormKey != space.SpaceKey ? RecordNames.Describe(cell) : null,
            Transform: new PlacedTransform(
                Geometry.ToVector(placement.Position),
                Geometry.RotationFromEuler(placement.Rotation),
                Geometry.NormalizeScale(record.Scale)),
            Base: record.GetBaseRef(),
            IsTeleportDoor: record is IPlacedObjectGetter { TeleportDestination: not null },
            RotationRadians: placement.Rotation));
        _scan.TargetLocations.Add(new TargetLocation(_winningCells[cell.FormKey], persistent));
    }

    private void AddOther(IPlacedGetter record, IPlacementGetter placement, FormKey spaceKey, ModKey winningMod)
    {
        if (!_scan.OthersBySpace.TryGetValue(spaceKey, out var others))
        {
            others = [];
            _scan.OthersBySpace[spaceKey] = others;
        }
        others.Add(new OtherObject(
            record.FormKey,
            winningMod,
            _config.Verbose ? record.EditorID : null,
            record.GetBaseRef(),
            Geometry.ToVector(placement.Position),
            placement.Rotation,
            Geometry.NormalizeScale(record.Scale),
            record is IPlacedObjectGetter { Primitive: not null }));
    }

    /// <summary>
    /// Comparison space of a cell: its worldspace for exterior cells (including the worldspace's
    /// persistent TopCell, whose context parent is the worldspace), otherwise the interior cell itself.
    /// </summary>
    private readonly record struct CellSpace(FormKey SpaceKey, IMajorRecordGetter SpaceRecord);

    private static CellSpace GetCellSpace(IModContext<ISkyrimMod, ISkyrimModGetter, ICell, ICellGetter> cellContext)
    {
        for (var current = cellContext.Parent; current != null; current = current.Parent)
        {
            if (current.Record is IWorldspaceGetter worldspace) return new CellSpace(worldspace.FormKey, worldspace);
        }
        return new CellSpace(cellContext.Record.FormKey, cellContext.Record);
    }
}
