using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Spatial;

/// <summary>A record named by links of different kinds is measured per kind, whichever kind is asked first.</summary>
public class BaseShapeLinkKindTests
{
    private static readonly ModKey Mod = ModKey.FromNameAndExtension("LinkKinds.esp");

    private static readonly TestStatic Crate = new(
        new FormKey(Mod, 0x801), @"test\linkkindcrate.nif", TestMeshes.BoxTriangles(new Box(new Vector3(-50), new Vector3(50))));

    private static readonly BaseKey AsNpc = Crate.Base with { Kind = BaseLinkKind.Npc };

    [Fact]
    public void AStaticNamedAsAnNpcFirstIsStillMeasuredAsAPlaceableObject()
    {
        var shapes = TestShapes.Create(Mod, "BaseShapeLinkKindData", Crate);

        Assert.False(shapes.Of(AsNpc).Resolved);
        Assert.True(shapes.Of(Crate.Base).Resolved);
    }

    [Fact]
    public void AStaticNamedAsAPlaceableObjectFirstIsStillUnresolvedAsAnNpc()
    {
        var shapes = TestShapes.Create(Mod, "BaseShapeLinkKindData", Crate);

        Assert.True(shapes.Of(Crate.Base).Resolved);
        Assert.False(shapes.Of(AsNpc).Resolved);
    }
}
