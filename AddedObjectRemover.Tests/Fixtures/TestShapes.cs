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
    /// <param name="dataFolderName">A Data folder next to the test binaries, one per test class so parallel classes do not share files.</param>
    public static BaseObjectShapeProvider Create(ModKey mod, string dataFolderName, params TestStatic[] statics)
    {
        var skyrimMod = new SkyrimMod(mod, SkyrimRelease.SkyrimSE);
        foreach (var model in statics)
        {
            skyrimMod.Statics.Add(new Static(model.FormKey, SkyrimRelease.SkyrimSE) { Model = new Model { File = model.Model } });
        }

        var dataPath = Path.Combine(AppContext.BaseDirectory, dataFolderName);
        foreach (var model in statics) WriteMesh(dataPath, model);

        var messages = new MeshMessageLog(enabled: false);
        var meshFiles = new MeshFileSource(dataPath, GameRelease.SkyrimSE, [mod], messages);
        return new BaseObjectShapeProvider(skyrimMod.ToImmutableLinkCache(), meshFiles, messages);
    }

    public static OtherObject Placed(ModKey mod, int index, BaseRef baseRef, Vector3 position, float zRadians = 0f) =>
        new(new FormKey(mod, 0x900 + (uint)index), mod, EditorId: null, baseRef, position, new P3Float(0, 0, zRadians), 1f,
            IsPrimitive: false, HasMapMarker: false);

    private static void WriteMesh(string dataPath, TestStatic model)
    {
        var nif = TestNifs.CreateWithRoot();
        TestNifs.AddShape(nif, TestNifs.Root(nif), model.Triangles);
        var meshPath = Path.Combine(dataPath, MeshFileSource.NormalizeMeshPath(model.Model));
        Directory.CreateDirectory(Path.GetDirectoryName(meshPath)!);
        File.WriteAllBytes(meshPath, TestNifs.Save(nif));
    }
}
