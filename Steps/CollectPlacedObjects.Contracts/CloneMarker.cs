using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>What a clone's marker names: the object it copies and the cleaned mod whose content it belongs to.</summary>
public readonly record struct CloneMark(RecordKey Original, PluginName CleanedMod);

/// <summary>What an EditorID says as a clone marker.</summary>
/// <param name="StartsLikeMarker">It starts like a marker, so it is one or a broken one.</param>
/// <param name="Mark">Null when it is no marker exactly as <see cref="CloneMarker.Of"/> writes it.</param>
public readonly record struct CloneMarkerReading(bool StartsLikeMarker, CloneMark? Mark)
{
    public bool IsBroken => StartsLikeMarker && Mark is null;
}

/// <summary>
/// The EditorID that marks a clone, a placed record the restore adds to the patch as a copy of another mod's object at the cleaned
/// mod's place: "AORClone_", the original's local FormID in 6 hex digits, "_", the original's plugin name, "_", the cleaned mod's name.
/// Each name is written as the hex of its lower-case UTF-8 bytes, so the EditorID holds only letters, digits and underscores, which
/// the Creation Kit keeps, and is the same however the load order spells the names. When that EditorID would be longer than an
/// EditorID may be, each name is written as "H" and the hex of the first bytes of the SHA-256 of those bytes instead, which only the
/// plugins of the load order can be matched against.
/// </summary>
public static class CloneMarker
{
    private const string Prefix = "AORClone_";
    private const char Separator = '_';
    private const string IdFormat = "X6";

    /// <summary>The longest EditorID the marker writes.</summary>
    private const int LongestEditorId = 255;

    private const char HashedName = 'H';

    /// <summary>The bytes of the hash a hashed name keeps: enough that no two plugin names of a load order share them.</summary>
    private const int HashedNameBytes = 8;

    public static string Of(RecordKey original, PluginName cleanedMod) =>
        MarkerOf(original, HexOf(original.Plugin), HexOf(cleanedMod)) is { Length: <= LongestEditorId } spelled
            ? spelled
            : MarkerOf(original, HashedNameOf(original.Plugin), HashedNameOf(cleanedMod));

    /// <summary>How the marker writes a plugin name it hashes; the reader matches the plugins of the load order against it.</summary>
    public static string HashedNameOf(PluginName plugin) =>
        HashedName + Convert.ToHexString(SHA256.HashData(LowerCaseBytesOf(plugin)), 0, HashedNameBytes);

    /// <param name="pluginOfHashedName">The plugin of the load order whose <see cref="HashedNameOf"/> is the text; null when none is, or more than one.</param>
    public static CloneMarkerReading Read(string? editorId, Func<string, PluginName?> pluginOfHashedName) =>
        editorId is not null && editorId.StartsWith(Prefix, StringComparison.Ordinal)
            ? new(StartsLikeMarker: true, MarkOf(editorId[Prefix.Length..].Split(Separator), editorId, pluginOfHashedName))
            : new(StartsLikeMarker: false, Mark: null);

    /// <param name="originalPlugin">The original's plugin name as the marker writes it.</param>
    /// <param name="cleanedMod">The cleaned mod's name as the marker writes it.</param>
    private static string MarkerOf(RecordKey original, string originalPlugin, string cleanedMod) =>
        $"{Prefix}{original.Id.ToString(IdFormat, CultureInfo.InvariantCulture)}{Separator}{originalPlugin}{Separator}{cleanedMod}";

    /// <param name="parts">The EditorID after the prefix, split at each separator.</param>
    /// <returns>Null when the EditorID is no marker exactly as <see cref="Of"/> writes it.</returns>
    private static CloneMark? MarkOf(string[] parts, string editorId, Func<string, PluginName?> pluginOfHashedName) =>
        parts is [var id, var original, var cleaned]
        && uint.TryParse(id, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var number)
        && NameOf(original, pluginOfHashedName) is { } originalPlugin
        && NameOf(cleaned, pluginOfHashedName) is { } cleanedMod
        && Of(new RecordKey(originalPlugin, number), cleanedMod) == editorId
            ? new CloneMark(new RecordKey(originalPlugin, number), cleanedMod)
            : null;

    private static string HexOf(PluginName plugin) => Convert.ToHexString(LowerCaseBytesOf(plugin));

    private static byte[] LowerCaseBytesOf(PluginName plugin) => Encoding.UTF8.GetBytes(plugin.FileName.ToLowerInvariant());

    /// <returns>Null when the text is neither the hex of a name nor the hashed name of a plugin of the load order.</returns>
    private static PluginName? NameOf(string written, Func<string, PluginName?> pluginOfHashedName) =>
        written.StartsWith(HashedName) ? pluginOfHashedName(written)
        : written.Length > 0 && written.Length % 2 == 0 && written.All(char.IsAsciiHexDigit) ? new PluginName(Encoding.UTF8.GetString(Convert.FromHexString(written)))
        : null;
}
