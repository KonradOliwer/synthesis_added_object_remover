using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>Created in the entry from the Synthesis state's Data folder, game release and listed plugins.</summary>
/// <param name="listedPlugins">The plugins whose archives count, in load order.</param>
internal sealed class MeshFilesFactory(string dataFolder, GameRelease release, IReadOnlyList<ModKey> listedPlugins) : IMeshFilesFactory
{
    public IMeshFiles Open(ShapeInclusion inclusion)
    {
        var problems = new AssetProblemLog();
        return new MeshFiles(new MeshFileSource(dataFolder, release, listedPlugins, problems), new NifGeometryReader(), problems, inclusion);
    }
}
