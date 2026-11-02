using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>Created in the entry from the Synthesis state's Data folder and game release and the final world's plugins, whose archives it reads.</summary>
/// <param name="listedPlugins">The plugins whose archives count, in load order.</param>
public sealed class MeshFilesFactory(string dataFolder, GameRelease release, IReadOnlyList<PluginName> listedPlugins) : IMeshFilesFactory
{
    public IMeshFiles Open(ShapeInclusion inclusion)
    {
        var problems = new AssetProblemLog();
        IReadOnlyList<ModKey> loadOrder = [.. listedPlugins.Select(plugin => ModKey.FromFileName(plugin.FileName))];
        return new MeshFiles(
            new MeshFileSource(dataFolder, release, loadOrder, problems), new NifGeometryReader(), problems, inclusion);
    }
}
