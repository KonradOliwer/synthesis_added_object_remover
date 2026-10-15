using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using Mutagen.Bethesda.Plugins.Cache;

namespace AddedObjectRemover.Tests.Fixtures;

internal static class TestNpcBodies
{
    /// <summary>A body cache that follows the Traits template flag, as the patcher does.</summary>
    /// <param name="bodyBounds">The body mesh bounds to use; by default new ones over the catalog's mesh files.</param>
    public static NpcBodyCache Create(ILinkCache linkCache, IBaseObjectShapes shapes, IBodyMeshBounds? bodyBounds = null)
    {
        var meshFiles = TestShapes.MeshFilesOf(shapes);
        var rules = TooCloseObjects.NpcBodyRules;
        return new(
            new NpcRecordsPlugin(linkCache),
            rules.TemplateFlag,
            rules.CreateOwnBodyResolver(meshFiles, bodyBounds ?? new BodyMeshBounds(meshFiles)),
            rules.DistinctBodies);
    }
}
