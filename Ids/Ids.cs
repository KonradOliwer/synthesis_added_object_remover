namespace AddedObjectRemover;

/// <summary>A target object's position in the collected target list, which is in <see cref="RecordKeyOrder"/>.</summary>
public readonly record struct TargetId(int Index) : IComparable<TargetId>
{
    public int CompareTo(TargetId other) => Index.CompareTo(other.Index);
}

/// <summary>
/// Another mod's object, dense over the collected other-mod objects and then the support-only objects,
/// each list in <see cref="RecordKeyOrder"/>.
/// </summary>
public readonly record struct OtherId(int Index) : IComparable<OtherId>
{
    public int CompareTo(OtherId other) => Index.CompareTo(other.Index);
}

/// <summary>A plugin file name such as "Example.esp"; equal ignoring case, like the plugin names Mutagen compares.</summary>
public readonly struct PluginName: IEquatable<PluginName>
{
    private static readonly int EmptyHash = StringComparer.OrdinalIgnoreCase.GetHashCode(string.Empty);

    private readonly int _hash;

    public PluginName(string fileName)
    {
        FileName = fileName;
        _hash = StringComparer.OrdinalIgnoreCase.GetHashCode(fileName);
    }

    /// <summary>Null only for a default value.</summary>
    public string FileName { get; }

    public bool Equals(PluginName other) => StringComparer.OrdinalIgnoreCase.Equals(FileName, other.FileName);

    public override bool Equals(object? obj) => obj is PluginName other && Equals(other);

    public override int GetHashCode() => FileName is null ? EmptyHash : _hash;

    public override string ToString() => FileName;

    public static bool operator ==(PluginName first, PluginName second) => first.Equals(second);

    public static bool operator !=(PluginName first, PluginName second) => !first.Equals(second);
}

/// <summary>A record's identity: the plugin that first defines it and its local FormID. Prints as "00ABCD:Example.esp".</summary>
public readonly record struct RecordKey(PluginName Plugin, uint Id)
{
    public override string ToString() => $"{Id:X6}:{Plugin}";
}

/// <summary>The kind of record a placed record's base link expects.</summary>
public enum BaseLinkKind
{
    PlaceableObject,
    Npc,
    Hazard,
    Projectile,
}

/// <summary>A base record a placed object refers to, with the kind of record the reference expects.</summary>
public readonly record struct BaseKey(RecordKey Record, BaseLinkKind Kind);

/// <summary>
/// The order every "first" and "lowest" choice follows: origin plugin name ignoring case, then local FormID. It
/// does not depend on the load order, so the choices stay the same when the load order changes.
/// </summary>
public static class RecordKeyOrder
{
    public static IComparer<RecordKey> Comparer { get; } = Comparer<RecordKey>.Create(Compare);

    private static int Compare(RecordKey first, RecordKey second)
    {
        var byPlugin = StringComparer.OrdinalIgnoreCase.Compare(first.Plugin.FileName, second.Plugin.FileName);
        return byPlugin != 0 ? byPlugin : first.Id.CompareTo(second.Id);
    }
}

/// <summary>
/// Orders record keys by their printed text, ordinally, so the FormID comes first. It deliberately
/// differs from <see cref="RecordKeyOrder"/>.
/// </summary>
public static class RecordKeyTextOrder
{
    // The FormID prints as at least 6 uppercase hex digits (8 at most for 32 bits), then ':'.
    private const int MaxIdPrefixLength = 9;
    private const string IdFormat = "X6";
    private const char IdSeparator = ':';

    public static IComparer<RecordKey> Comparer { get; } = Comparer<RecordKey>.Create(Compare);

    /// <remarks>The ':' ends each id text, so neither id text is a prefix of the other unless they are equal.</remarks>
    private static int Compare(RecordKey first, RecordKey second)
    {
        Span<char> firstPrefix = stackalloc char[MaxIdPrefixLength];
        Span<char> secondPrefix = stackalloc char[MaxIdPrefixLength];
        var byId = FormatIdPrefix(first.Id, firstPrefix).SequenceCompareTo(FormatIdPrefix(second.Id, secondPrefix));
        return byId != 0 ? byId : string.CompareOrdinal(first.Plugin.FileName, second.Plugin.FileName);
    }

    private static ReadOnlySpan<char> FormatIdPrefix(uint id, Span<char> buffer)
    {
        id.TryFormat(buffer, out var length, IdFormat);
        buffer[length] = IdSeparator;
        return buffer[..(length + 1)];
    }
}
