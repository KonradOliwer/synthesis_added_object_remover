using System.Numerics;

namespace AddedObjectRemover.Tests.FlatAreaNearestPoint;

public class NearestFreePointTests
{
    private const float Radius = 100f;
    private const float Clearance = 10f;

    private static readonly Func<FlatRectangle, IEnumerable<FlatRectangle>> NoRectangles = _ => [];

    private static OrientedBox Box(float x, float y, float halfSize) =>
        new(new Vector3(x, y, 0), Mat3.Identity, new Vector3(halfSize, halfSize, 5));

    private static Vector2? Find(
        Vector2 centre,
        FlatRectangle? clip = null,
        Func<FlatRectangle, IEnumerable<FlatRectangle>>? rectangles = null,
        params OrientedBox[] boxes) =>
        NearestFreePoint.Find(centre, Radius, clip, rectangles ?? NoRectangles, boxes, Clearance);

    [Fact]
    public void FreeCentreIsItsOwnNearestPoint()
    {
        var found = Find(new Vector2(5, 7));
        Assert.Equal(new Vector2(5, 7), found!.Value);
    }

    [Fact]
    public void CentreInsideABoxMovesToTheBoxEdgeGrownByTheClearance()
    {
        var found = Find(new Vector2(18, 0), boxes: Box(0, 0, 20));
        Assert.Equal(30f, found!.Value.X, 0.5f);
        Assert.Equal(0f, found.Value.Y, 0.5f);
    }

    [Fact]
    public void BoxFarOutsideTheClearanceBlocksNothing()
    {
        var found = Find(new Vector2(0, 0), boxes: Box(60, 0, 10));
        Assert.Equal(Vector2.Zero, found!.Value);
    }

    [Fact]
    public void NothingIsFoundWhenTheWholeDiscIsBlocked()
    {
        Assert.Null(Find(new Vector2(0, 0), boxes: Box(0, 0, 500)));
    }

    [Fact]
    public void ClipKeepsTheResultInsideTheRectangle()
    {
        var clip = new FlatRectangle(new Vector2(20, -50), new Vector2(80, 50));
        var found = Find(new Vector2(0, 0), clip);
        Assert.Equal(20f, found!.Value.X, 1e-3f);
        Assert.Equal(0f, found.Value.Y, 1e-3f);
    }

    [Fact]
    public void ClipOutsideTheDiscLeavesNothing()
    {
        var clip = new FlatRectangle(new Vector2(500, 500), new Vector2(600, 600));
        Assert.Null(Find(new Vector2(0, 0), clip));
    }

    [Fact]
    public void BlockedRectanglesAreAskedForTheBoundsOfTheClippedDisc()
    {
        FlatRectangle? asked = null;
        var clip = new FlatRectangle(new Vector2(-1000, -1000), new Vector2(0, 1000));
        Find(new Vector2(0, 0), clip, rectangles: bounds =>
        {
            asked = bounds;
            return [];
        });
        Assert.Equal(-Radius, asked!.Value.Min.X, 0.5f);
        Assert.Equal(0f, asked.Value.Max.X, 0.5f);
        Assert.Equal(Radius, asked.Value.Max.Y, 0.5f);
    }

    [Fact]
    public void BlockedRectangleMovesThePointOutOfIt()
    {
        var blocked = new FlatRectangle(new Vector2(-30, -30), new Vector2(30, 30));
        var found = Find(new Vector2(0, 0), rectangles: _ => [blocked]);
        Assert.Equal(40f, Vector2.Distance(Vector2.Zero, found!.Value), 0.5f);
    }
}
