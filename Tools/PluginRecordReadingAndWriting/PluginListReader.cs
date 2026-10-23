using System.Text.RegularExpressions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Noggog;

namespace AddedObjectRemover;

/// <summary>
/// Reads the game's plugin list the way Synthesis does: the implicit plugins, the Creation Club plugins and the plugins file, every
/// entry with its enabled flag. The patch's own entry may be missing from the list; every other plugin of the Synthesis state must
/// be listed above it.
/// </summary>
public static partial class PluginListReader
{
    /// <param name="pluginsFile">The plugins file to read; null reads the library's default location for the game.</param>
    /// <param name="statePlugins">The plugins Synthesis loaded; the patch among them may be missing from the list.</param>
    public static PluginListRead Read(
        GameRelease release, DirectoryPath dataFolder, FilePath? pluginsFile, IEnumerable<ModKey> statePlugins, ModKey patch)
    {
        if (FindPluginsFile(release, pluginsFile) is not { } path) return new PluginListRead.Failed(new PluginListProblem.PathUnknown());
        if (!File.Exists(path)) return new PluginListRead.Failed(new PluginListProblem.FileMissing(path));
        var read = ReadListings(release, dataFolder, pluginsFile, path);
        return read is PluginListRead.Found found && FindStatePluginProblem(found, statePlugins, patch) is { } problem
            ? new PluginListRead.Failed(problem)
            : read;
    }

    /// <returns>Null when the library cannot tell where the game keeps its plugins file.</returns>
    private static string? FindPluginsFile(GameRelease release, FilePath? pluginsFile)
    {
        if (pluginsFile is { } given) return given.Path;
        try
        {
            return PluginListings.GetListingsPath(release);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <param name="path">The plugins file that <paramref name="pluginsFile"/> names, or the library's default one.</param>
    private static PluginListRead ReadListings(GameRelease release, DirectoryPath dataFolder, FilePath? pluginsFile, string path)
    {
        try
        {
            return new PluginListRead.Found(
                path,
                [.. pluginsFile is { } file
                    ? LoadOrder.GetLoadOrderListings(release, file, null, dataFolder)
                    : LoadOrder.GetLoadOrderListings(release, dataFolder)]);
        }
        catch (InvalidDataException ex)
        {
            return new PluginListRead.Failed(new PluginListProblem.MalformedLine(path, LineNumberOf(ex)));
        }
    }

    /// <remarks>The library lists each plugin once, so every plugin has one position.</remarks>
    /// <returns>Null when every plugin of the state other than the patch is listed above the patch's entry.</returns>
    private static PluginListProblem? FindStatePluginProblem(PluginListRead.Found found, IEnumerable<ModKey> statePlugins, ModKey patch)
    {
        var positions = found.Listings.Select((listing, position) => (listing.ModKey, position)).ToDictionary(entry => entry.ModKey, entry => entry.position);
        var patchPosition = positions.GetValueOrDefault(patch, found.Listings.Length);
        foreach (var plugin in statePlugins.Where(plugin => plugin != patch))
        {
            if (!positions.TryGetValue(plugin, out var position)) return new PluginListProblem.PluginNotListed(found.FilePath, plugin.ToPluginName());
            if (position > patchPosition) return new PluginListProblem.PluginListedAfterPatch(found.FilePath, plugin.ToPluginName(), patch.ToPluginName());
        }
        return null;
    }

    /// <remarks>The library gives the line number only inside the text of its exception; text without it means the library changed.</remarks>
    private static int LineNumberOf(InvalidDataException ex)
    {
        var match = LineNumberPattern().Match(ex.Message);
        return match.Success ? int.Parse(match.Groups[1].Value) : throw new InvalidOperationException($"The library's malformed-line error names no line: {ex.Message}", ex);
    }

    [GeneratedRegex(@"line\s(\d+)")]
    private static partial Regex LineNumberPattern();
}
