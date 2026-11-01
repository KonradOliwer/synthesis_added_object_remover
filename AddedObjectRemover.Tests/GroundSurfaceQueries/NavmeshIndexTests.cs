using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.GroundSurfaceQueries;

/// <summary>Which navmeshes a box reaches: an exact test against the turned box, with the navmeshes the caller includes.</summary>
public sealed class NavmeshIndexTests
{
    private static readonly RecordKey Navmesh = new FormKey(TestTargets.TargetMod, 0x600).ToRecordKey();

    /// <summary>A long thin box turned 45 degrees about Z, so its long side runs diagonally.</summary>
    private static readonly OrientedBox TurnedBox = new(Vector3.Zero, Mat3.FromEuler(new Vector3(0, 0, MathF.PI / 4)), new Vector3(50, 5, 5));

    /// <summary>On the box's long axis, inside it.</summary>
    private static readonly Vector3 AlongTheBox = TurnedBox.Rotation.Transform(new Vector3(42, 0, 0));

    /// <summary>As far out across the box: outside it, but inside its bounds along the world axes.</summary>
    private static readonly Vector3 AcrossTheBox = TurnedBox.Rotation.Transform(new Vector3(0, 42, 0));

    [Fact]
    public void ATriangleReachesTheTurnedBoxOnlyWhereTheBoxItselfIsNotJustItsBounds()
    {
        Assert.True(TurnedBox.WorldAabb(0f).Contains(AcrossTheBox));
        Assert.True(IndexOf(SmallTriangle(AlongTheBox)).AnyTriangleIn(TestTargets.Space, TurnedBox, _ => true));
        Assert.False(IndexOf(SmallTriangle(AcrossTheBox)).AnyTriangleIn(TestTargets.Space, TurnedBox, _ => true));
    }

    [Fact]
    public void ANavmeshTheCallerLeavesOutReachesNothing()
    {
        var index = IndexOf(SmallTriangle(AlongTheBox));

        Assert.False(index.AnyTriangleIn(TestTargets.Space, TurnedBox, navmesh => navmesh != Navmesh));
    }

    /// <summary>A triangle lying flat on the box's top face touches it; one a little above does not, as the box is not grown.</summary>
    [Theory]
    [InlineData(5f, true)]
    [InlineData(5.5f, false)]
    public void ATriangleOnTheBoxsFaceTouchesItAndOneJustAboveDoesNot(float height, bool reached)
    {
        var index = IndexOf(SmallTriangle(new Vector3(0, 0, height)));

        Assert.Equal(reached, index.AnyTriangleIn(TestTargets.Space, TurnedBox, _ => true));
    }

    private static NavmeshIndex IndexOf(MeshTriangle triangle) =>
        new(bucket => bucket.Grid is null ? [new NavmeshWorldTriangle(Navmesh, triangle)] : null);

    private static MeshTriangle SmallTriangle(Vector3 centre) =>
        new(centre + new Vector3(-1, -1, 0), centre + new Vector3(1, -1, 0), centre + new Vector3(0, 1, 0));
}
