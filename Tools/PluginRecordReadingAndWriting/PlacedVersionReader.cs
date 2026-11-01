using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Reads the versions of the placed records that became facts while the load order is walked highest priority first: the winner,
/// then the lower versions down to and including the first one whose plugin stops the walk. For a record the read's plugin
/// overrides, no plugin stops the walk before the version just below the read's plugin's is kept, and the parts of the versions
/// from that one up are compared (<see cref="PlacedRecordPartComparison"/>).
/// </summary>
internal sealed class PlacedVersionReader(PluginName target, PluginNames names, TargetFootprint footprint, Func<PluginName, bool> stopsTheWalk, PlacedRecordParts parts)
{
    private readonly PluginName _target = target;
    private readonly PluginNames _names = names;
    private readonly TargetFootprint _footprint = footprint;
    private readonly Func<PluginName, bool> _stopsTheWalk = stopsTheWalk;
    private readonly PlacedRecordParts _parts = parts;

    /// <summary>Each fact's versions, highest first.</summary>
    private readonly Dictionary<RecordKey, List<PlacedVersionFact>> _versions = new();

    /// <summary>
    /// The version lists no plugin has stopped yet. Memory stays bounded: only records that became facts keep versions, and each
    /// keeps them only down to the first version whose plugin stops the walk.
    /// </summary>
    private readonly Dictionary<RecordKey, List<PlacedVersionFact>> _openVersions = new();

    private readonly Dictionary<RecordKey, IPlacedGetter> _earlierRecords = new();

    /// <summary>
    /// The versions, highest first, of each record the read's plugin overrides whose version just below the read's plugin's is not
    /// read yet. Memory stays bounded: each list is dropped once that version is read and the parts are compared.
    /// </summary>
    private readonly Dictionary<RecordKey, List<IPlacedGetter>> _versionsToCompare = new();
    private readonly Dictionary<RecordKey, PlacedPartDifferences> _partDifferences = new();

    /// <summary>For each record the read's plugin overrides: the version just below the read's plugin's, as its plugin stores it.</summary>
    public IReadOnlyDictionary<RecordKey, IPlacedGetter> EarlierRecords => _earlierRecords;

    public void StartVersions(IPlacedGetter record, RecordKey key, PlacedVersionFact winner)
    {
        var versions = new List<PlacedVersionFact> { winner };
        _versions.Add(key, versions);
        if (_footprint.OverriddenRecords.Contains(key)) _versionsToCompare.Add(key, [record]);
        if (!StopsTheWalk(key, winner.Plugin)) _openVersions.Add(key, versions);
    }

    /// <remarks>A plugin that holds the record twice (broken data) gives one version, the first found, as it does for the winner.</remarks>
    public void KeepLowerVersion(IPlacedGetter record, RecordKey key, ScannedCell cell)
    {
        if (!_openVersions.TryGetValue(key, out var versions) || versions[^1].Plugin == cell.Plugin) return;
        var isBelowTarget = versions[^1].Plugin == _target;
        if (isBelowTarget && _footprint.OverriddenRecords.Contains(key)) _earlierRecords.Add(key, record);
        versions.Add(ReadVersion(record, cell));
        if (_versionsToCompare.TryGetValue(key, out var versionsToCompare)) KeepVersionToCompare(record, key, versionsToCompare, isBelowTarget, versions);
        if (StopsTheWalk(key, cell.Plugin)) _openVersions.Remove(key);
    }

    /// <summary>Once the version just below the read's plugin's is read, compares the parts of the record's versions.</summary>
    /// <param name="versions">The versions read so far, highest first; <paramref name="versionsToCompare"/> holds the same versions as stored.</param>
    private void KeepVersionToCompare(
        IPlacedGetter record, RecordKey key, List<IPlacedGetter> versionsToCompare, bool isBelowTarget, List<PlacedVersionFact> versions)
    {
        versionsToCompare.Add(record);
        if (!isBelowTarget) return;
        _versionsToCompare.Remove(key);
        IPlacedGetter[] lowestFirst = [.. Enumerable.Reverse(versionsToCompare)];
        PluginName[] plugins = [.. versions.Select(version => version.Plugin).Reverse()];
        _partDifferences.Add(key, PlacedRecordPartComparison.Of(_parts, lowestFirst, plugins));
    }

    /// <summary>A record the read's plugin overrides is walked on, whatever the plugin, until its version just below the read's plugin's is read.</summary>
    private bool StopsTheWalk(RecordKey key, PluginName plugin) => _stopsTheWalk(plugin) && !_versionsToCompare.ContainsKey(key);

    public PlacedVersionFact ReadVersion(IPlacedGetter record, ScannedCell cell) => new(
        cell.Plugin,
        new PlacementFacts(record.GetPresence(_names), record.Placement?.GetPosition(), record.Placement?.GetRotation()),
        record.Scale,
        record.GetBaseKey(_names),
        cell.Cell,
        cell.Space.Key,
        record.IsPersistent(),
        new ReferenceFacts(
            record is IPlacedObjectGetter { TeleportDestination: not null },
            record is IPlacedObjectGetter { Primitive: not null },
            record is IPlacedObjectGetter { MapMarker: not null },
            record.GetReferenceRadius(),
            record.GetPrimitiveBounds(),
            record.GetCollisionLayer(),
            record.IsCullingVolume()));

    /// <summary>The fact with its versions, lowest first, and how their parts differ when the read's plugin overrides it.</summary>
    public PlacedRecordFact WithVersions(PlacedRecordFact fact) => fact with
    {
        Versions = [.. Enumerable.Reverse(_versions[fact.Key])],
        PartDifferences = _partDifferences.TryGetValue(fact.Key, out var differences) ? differences : null,
    };
}
