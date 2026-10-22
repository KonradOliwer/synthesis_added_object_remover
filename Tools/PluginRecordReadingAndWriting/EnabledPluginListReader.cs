using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Noggog;

namespace AddedObjectRemover;

/// <summary>
/// Reads the game's plugin list the way Synthesis does: the implicit plugins, the Creation Club plugins and the plugins file, each with
/// its enabled flag. The patch's own entry may be missing from the list; every other plugin of the Synthesis state must be in it.
/// </summary>
public static partial class EnabledPluginListReader
{
    /// <param name="pluginsFile">The plugins file to read; null reads the library's default location for the game.</param>
    /// <param name="statePlugins">The plugins Synthesis loaded; the patch among them may be missing from the list.</param>
    public static PluginListRead Read(
        GameRelease release, DirectoryPath dataFolder, FilePath? pluginsFile, IEnumerable<ModKey> statePlugins, ModKey patch)
    {
        if (FindPluginsFile(release, pluginsFile) is not { } path) return new PluginListRead.Failed(new PluginListProblem.PathUnknown());
        if (!File.Exists(path)) return new PluginListRead.Failed(new PluginListProblem.FileMissing(path));

        ImmutableArray<ILoadOrderListingGetter> listings;
        try
        {
            listings = [.. pluginsFile is { } file
                ? LoadOrder.GetLoadOrderListings(release, file, null, dataFolder)
                : LoadOrder.GetLoadOrderListings(release, dataFolder)];
        }
        catch (InvalidDataException ex)
        {
            return new PluginListRead.Failed(new PluginListProblem.MalformedLine(path, LineNumberOf(ex)));
        }

        var listed = listings.Select(listing => listing.ModKey).ToHashSet();
        var unlisted = statePlugins.Where(plugin => plugin != patch && !listed.Contains(plugin)).Select(plugin => (ModKey?)plugin).FirstOrDefault();
        return unlisted is { } missing
            ? new PluginListRead.Failed(new PluginListProblem.PluginNotListed(path, missing.ToPluginName()))
            : new PluginListRead.Found(path, listings);
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

    /// <remarks>The library gives the line number only inside the text of its exception; text without it means the library changed.</remarks>
    private static int LineNumberOf(InvalidDataException ex)
    {
        var match = LineNumberPattern().Match(ex.Message);
        return match.Success ? int.Parse(match.Groups[1].Value) : throw new InvalidOperationException($"The library's malformed-line error names no line: {ex.Message}", ex);
    }

    [GeneratedRegex(@"line\s(\d+)")]
    private static partial Regex LineNumberPattern();
}
