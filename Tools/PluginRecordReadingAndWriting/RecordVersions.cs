using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>A record's versions as their plugins store them, lowest first: the one just below the named plugin's, the named plugin's, then each later one up to the winner.</summary>
internal sealed record RecordVersions<TVersion>(ImmutableArray<PluginName> Plugins, ImmutableArray<TVersion> Versions)
{
    public TVersion Earlier => Versions[0];

    public TVersion OfNamedPlugin => VersionComparison.NamedPluginVersionOf(Versions);

    public TVersion Winner => Versions[^1];

    public PluginName WinningMod => Plugins[^1];
}

/// <summary>
/// Keeps the versions of records the named plugin overrides while the load order is walked highest priority first, until the version
/// just below the named plugin's is read; no plugin stops the walk before. Memory stays bounded: only the records asked for keep
/// versions.
/// </summary>
/// <typeparam name="TVersion">One version of the record as read.</typeparam>
/// <typeparam name="TPlace">Where the winning version stands, as the caller needs it.</typeparam>
internal sealed class NamedPluginOverrideWalk<TVersion, TPlace>(PluginName namedPlugin)
{
    private readonly Dictionary<RecordKey, (TPlace Place, List<(PluginName Plugin, TVersion Version)> Versions)> _open = new();
    private readonly Dictionary<RecordKey, (TPlace Place, RecordVersions<TVersion> Versions)> _read = new();
    private readonly List<RecordKey> _readOrder = [];

    /// <param name="place">Kept from the first version found, the winner.</param>
    /// <remarks>A plugin that holds the record twice (broken data) gives one version, the first found, as it does for placed records.</remarks>
    public void Keep(RecordKey key, PluginName plugin, TVersion version, TPlace place)
    {
        if (_read.ContainsKey(key)) return;
        if (!_open.TryGetValue(key, out var open))
        {
            open = (place, []);
            _open.Add(key, open);
        }
        var versions = open.Versions;
        if (versions.Count > 0 && versions[^1].Plugin == plugin) return;

        var isBelowNamedPlugin = versions.Count > 0 && versions[^1].Plugin == namedPlugin;
        versions.Add((plugin, version));
        if (!isBelowNamedPlugin) return;

        _open.Remove(key);
        var lowestFirst = Enumerable.Reverse(versions).ToList();
        _read.Add(key, (open.Place, new RecordVersions<TVersion>([.. lowestFirst.Select(entry => entry.Plugin)], [.. lowestFirst.Select(entry => entry.Version)])));
        _readOrder.Add(key);
    }

    /// <summary>The records whose version just below the named plugin's was read, in the order their walk ended.</summary>
    public IEnumerable<(RecordKey Key, TPlace Place, RecordVersions<TVersion> Versions)> Read =>
        _readOrder.Select(key => (key, _read[key].Place, _read[key].Versions));

    /// <summary>The records the named plugin itself defines, walked down to its own version, which is their lowest; lowest first.</summary>
    public IEnumerable<(RecordKey Key, TPlace Place, ImmutableArray<PluginName> Plugins, ImmutableArray<TVersion> Versions)> DefinedByNamedPlugin =>
        _open
            .Where(entry => entry.Key.Plugin == namedPlugin)
            .Select(entry => (
                entry.Key,
                entry.Value.Place,
                ImmutableArray.CreateRange(Enumerable.Reverse(entry.Value.Versions).Select(version => version.Plugin)),
                ImmutableArray.CreateRange(Enumerable.Reverse(entry.Value.Versions).Select(version => version.Version))));

    /// <param name="keys">Records whose version just below the named plugin's was read.</param>
    public IReadOnlyDictionary<RecordKey, RecordVersions<TVersion>> VersionsOf(IEnumerable<RecordKey> keys) =>
        keys.ToDictionary(key => key, key => _read[key].Versions);
}
