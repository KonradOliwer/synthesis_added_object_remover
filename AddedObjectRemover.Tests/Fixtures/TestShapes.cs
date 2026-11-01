using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using NiflySharp.Enums;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>A static base object whose model is a loose mesh of the given triangles.</summary>
/// <param name="Collision">The layer of the mesh's collision rigid body; null for a mesh without collision.</param>
/// <param name="Bounds">The record's object bounds, whole units; null for none set.</param>
/// <param name="IsMarker">The record carries the engine's IsMarker flag.</param>
internal sealed record TestStatic(FormKey FormKey, string Model, IReadOnlyList<MeshTriangle> Triangles, SkyrimLayer? Collision = null, Box? Bounds = null, bool IsMarker = false)
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
            var record = new Static(model.FormKey, SkyrimRelease.SkyrimSE) { Model = new Model { File = model.Model } };
            if (model.Bounds is { } bounds) record.ObjectBounds = new ObjectBounds { First = Corner(bounds.Min), Second = Corner(bounds.Max) };
            if (model.IsMarker) record.MajorFlags |= Static.MajorFlag.IsMarker;
            skyrimMod.Statics.Add(record);
        }

        var dataPath = Path.Combine(AppContext.BaseDirectory, dataFolderName);
        foreach (var model in statics) WriteMesh(dataPath, model.Model, model.Triangles, model.Collision);

        return Catalog(skyrimMod.ToImmutableLinkCache(), dataPath, mod);
    }

    private static Noggog.P3Int16 Corner(Vector3 corner) => new((short)corner.X, (short)corner.Y, (short)corner.Z);

    /// <summary>A catalog over the records of the link cache and the loose meshes in a Data folder, reading them through the real readers.</summary>
    public static IBaseObjectShapes Catalog(ILinkCache linkCache, string dataPath, ModKey mod)
    {
        var meshFiles = new MeshFilesFactory(dataPath, GameRelease.SkyrimSE, [mod]).Open(BaseObjectShapes.SolidShapes, new UnexpectedErrors());
        var shapes = new BaseObjectShapes(TestBaseFacts.Reader(linkCache), meshFiles, CollisionLayerReader.Read(linkCache));
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
            IsPrimitive: false, HasMapMarker: false, new ObjectChange(mod.ToPluginName(), AcceptedFrom: null), ReferenceRoles.None, ChangeWithoutCleanedMod: null);

    /// <summary>Writes a loose NIF of the triangles at the model path under the Data folder; without triangles it holds no shape to render.</summary>
    /// <param name="collision">The layer of a collision rigid body on the root node; null for none.</param>
    public static void WriteMesh(string dataPath, string model, IReadOnlyList<MeshTriangle> triangles, SkyrimLayer? collision = null)
    {
        var nif = TestNifs.CreateWithRoot();
        if (triangles.Count > 0) TestNifs.AddShape(nif, TestNifs.Root(nif), triangles);
        if (collision is { } layer) TestNifs.AddRigidBodyCollision(nif, TestNifs.Root(nif), layer);
        var meshPath = Path.Combine(dataPath, MeshFileSource.NormalizeMeshPath(model).Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(meshPath)!);
        File.WriteAllBytes(meshPath, TestNifs.Save(nif));
    }
}
