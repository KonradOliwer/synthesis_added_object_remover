using System.Numerics;
using NiflySharp;
using NiflySharp.Blocks;
using NiflySharp.Structs;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Skyrim SE NIF files built in memory with NiflySharp.</summary>
internal static class TestNifs
{
    public static readonly Matrix33 IdentityRotation = new() { M11 = 1, M22 = 1, M33 = 1 };

    public static NifFile CreateWithRoot() => new(NiVersion.GetSSE(), withRootNode: true);

    public static NiNode Root(NifFile nif) => nif.GetRootNode();

    /// <summary>An unskinned BSTriShape of the triangles (each with its own three vertices) as a child of <paramref name="parent"/>.</summary>
    public static BSTriShape AddShape(NifFile nif, NiNode parent, IReadOnlyList<MeshTriangle> triangles)
    {
        var vertices = triangles.SelectMany(t => new[] { t.A, t.B, t.C }).ToList();
        var indices = Enumerable.Range(0, triangles.Count)
            .Select(t => new Triangle((ushort)(3 * t), (ushort)(3 * t + 1), (ushort)(3 * t + 2)))
            .ToList();
        var shape = new BSTriShape(nif.Header.Version, vertices, indices, null, null)
        {
            IsSkinned = false,
            Rotation = IdentityRotation,
            Scale = 1,
        };
        AddChild(nif, parent, shape);
        return shape;
    }

    public static NiNode AddNode(NifFile nif, NiNode parent, string name, Vector3 translation)
    {
        var node = new NiNode
        {
            Name = new NiStringRef(name),
            Translation = translation,
            Rotation = IdentityRotation,
            Scale = 1,
        };
        AddChild(nif, parent, node);
        return node;
    }

    public static byte[] Save(NifFile nif)
    {
        using var stream = new MemoryStream();
        Assert.Equal(0, nif.Save(stream));
        return stream.ToArray();
    }

    private static void AddChild(NifFile nif, NiNode parent, INiObject child) =>
        parent.Children.AddBlockRef(nif.AddBlock(child));
}
