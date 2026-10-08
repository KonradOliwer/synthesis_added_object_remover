using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>A static base object whose model is a loose mesh of the given triangles.</summary>
internal sealed record TestStatic(FormKey FormKey, string Model, IReadOnlyList<MeshTriangle> Triangles)
{
    public BaseKey Base => new(FormKey.ToRecordKey(), BaseLinkKind.PlaceableObject);
}

/// <summary>Base object shapes read through the real mesh pipeline from in-memory statics and loose NIFs.</summary>
internal static class TestShapes
{
    /// <summary>The mesh reader behind each test catalog, so tests can read triangles the way the production code does.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IBaseObjectShapes, IMeshFiles> MeshFilesOfCatalog = new();

    public static IBaseObjectShapes Create(ModKey mod, string dataFolderName, params TestStatic[] statics) =>
        Create(new SkyrimMod(mod, SkyrimRelease.SkyrimSE), dataFolderName, statics);

    /// <param name="skyrimMod">Records the shapes may resolve besides the statics, which are added to it.</param>
    /// <param name="dataFolderName">A Data folder next to the test binaries, one per test class so parallel classes do not share files.</param>
    public static IBaseObjectShapes Create(SkyrimMod skyrimMod, string dataFolderName, params TestStatic[] statics)
    {
        var mod = skyrimMod.ModKey;
        foreach (var model in statics)
        {
            skyrimMod.Statics.Add(new Static(model.FormKey, SkyrimRelease.SkyrimSE) { Model = new Model { File = model.Model } });
        }

        var dataPath = Path.Combine(AppContext.BaseDirectory, dataFolderName);
        foreach (var model in statics) WriteMesh(dataPath, model.Model, model.Triangles);

        return Catalog(new BaseFactsReader(skyrimMod.ToImmutableLinkCache()), dataPath, mod);
    }

    /// <summary>A catalog over the loose meshes in a Data folder, reading them through the real mesh reader.</summary>
    public static IBaseObjectShapes Catalog(IBaseFacts bases, string dataPath, ModKey mod)
    {
        var meshFiles = new MeshFilesFactory(dataPath, GameRelease.SkyrimSE, [mod]).Open(BaseObjectRules.SolidShapes);
        var shapes = new BaseObjectShapes(bases, meshFiles);
        MeshFilesOfCatalog.Add(shapes, meshFiles);
        return shapes;
    }

    /// <summary>Lets <paramref name="wrapper"/> read meshes through the same reader as <paramref name="catalog"/>.</summary>
    public static void ShareMeshFiles(IBaseObjectShapes catalog, IBaseObjectShapes wrapper) => MeshFilesOfCatalog.Add(wrapper, MeshFilesOf(catalog));

    public static MeshTriangles? ReadTriangles(this IBaseObjectShapes shapes, string meshPath) => MeshFilesOf(shapes).ReadTriangles(meshPath);

    public static IMeshFiles MeshFilesOf(IBaseObjectShapes shapes) =>
        MeshFilesOfCatalog.TryGetValue(shapes, out var meshFiles) ? meshFiles : throw new InvalidOperationException("The catalog was not made by TestShapes.");

    public static OtherObject Placed(ModKey mod, int index, BaseKey baseKey, Vector3 position, float zRadians = 0f) =>
        new(new OtherId(index), new FormKey(mod, 0x900 + (uint)index).ToRecordKey(), TestTargets.Space, mod.ToPluginName(), EditorId: null, baseKey, position, new Vector3(0, 0, zRadians), 1f,
            IsPrimitive: false, HasMapMarker: false);

    /// <summary>Writes a loose NIF of the triangles at the model path under the Data folder.</summary>
    public static void WriteMesh(string dataPath, string model, IReadOnlyList<MeshTriangle> triangles)
    {
        var nif = TestNifs.CreateWithRoot();
        TestNifs.AddShape(nif, TestNifs.Root(nif), triangles);
        var meshPath = Path.Combine(dataPath, MeshFileSource.NormalizeMeshPath(model).Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(meshPath)!);
        File.WriteAllBytes(meshPath, TestNifs.Save(nif));
    }
}
