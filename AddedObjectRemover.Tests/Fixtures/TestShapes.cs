using System.Numerics;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>A static base object whose model is a loose mesh of the given triangles.</summary>
internal sealed record TestStatic(FormKey FormKey, string Model, IReadOnlyList<MeshTriangle> Triangles)
{
    public BaseRef Ref => new(FormKey, typeof(IStaticGetter));
}

/// <summary>Base object shapes read through the real mesh pipeline from in-memory statics and loose NIFs.</summary>
internal static class TestShapes
{
    public static ShapeCatalog Create(ModKey mod, string dataFolderName, params TestStatic[] statics) =>
        Create(new SkyrimMod(mod, SkyrimRelease.SkyrimSE), dataFolderName, statics);

    /// <param name="skyrimMod">Records the shapes may resolve besides the statics, which are added to it.</param>
    /// <param name="dataFolderName">A Data folder next to the test binaries, one per test class so parallel classes do not share files.</param>
    public static ShapeCatalog Create(SkyrimMod skyrimMod, string dataFolderName, params TestStatic[] statics)
    {
        var mod = skyrimMod.ModKey;
        foreach (var model in statics)
        {
            skyrimMod.Statics.Add(new Static(model.FormKey, SkyrimRelease.SkyrimSE) { Model = new Model { File = model.Model } });
        }

        var dataPath = Path.Combine(AppContext.BaseDirectory, dataFolderName);
        foreach (var model in statics) WriteMesh(dataPath, model.Model, model.Triangles);

        var problems = new AssetProblemLog();
        var meshFiles = new MeshFileSource(dataPath, GameRelease.SkyrimSE, [mod], problems);
        return new ShapeCatalog(new BaseFactsReader(skyrimMod.ToImmutableLinkCache()), meshFiles, problems);
    }

    public static OtherObject Placed(ModKey mod, int index, BaseRef baseRef, Vector3 position, float zRadians = 0f) =>
        new(new OtherId(index), new FormKey(mod, 0x900 + (uint)index), TestTargets.Space, mod, EditorId: null, baseRef, position, new P3Float(0, 0, zRadians), 1f,
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
