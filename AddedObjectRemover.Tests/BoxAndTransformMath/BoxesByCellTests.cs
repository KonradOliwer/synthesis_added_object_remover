using System.Numerics;

namespace AddedObjectRemover.Tests.BoxAndTransformMath;

/// <summary>Boxes found through the exterior cells their footprints reach, within their own space.</summary>
public class BoxesByCellTests
{
    private const float Cell = ExteriorGrid.CellSize;

    [Fact]
    public void ABoxIsFoundFromEveryCellItsFootprintReachesInItsOwnSpaceOnly()
    {
        var byCell = new BoxesByCell<string>(
        [
            ("A", Square(Cell - 100f, 200f)),
            ("A", Square(3 * Cell + Cell / 2, 50f)),
            ("B", Square(Cell - 100f, 200f)),
        ]);

        Assert.Equal([0], byCell.SharingACellWith("A", Square(Cell / 2, 10f)));
        Assert.Equal([0], byCell.SharingACellWith("A", Square(Cell + Cell / 2, 10f)));
        Assert.Equal([2], byCell.SharingACellWith("B", Square(Cell + Cell / 2, 10f)));
        Assert.Empty(byCell.SharingACellWith("A", Square(2 * Cell + Cell / 2, 10f)));
    }

    [Fact]
    public void ABoxSharingSeveralCellsIsListedOnceInAscendingOrder()
    {
        var byCell = new BoxesByCell<string>([("A", Square(5 * Cell, 10f)), ("A", Square(Cell, 300f))]);

        Assert.Equal([1], byCell.SharingACellWith("A", new Box(new Vector3(0, 0, 0), new Vector3(2 * Cell + 1, 2 * Cell + 1, 1))));
        Assert.Equal([0, 1], byCell.SharingACellWith("A", new Box(new Vector3(0, 0, 0), new Vector3(5 * Cell + 1, 5 * Cell + 1, 1))));
        Assert.Equal(Square(Cell, 300f), byCell.BoxAt(1));
    }

    [Fact]
    public void NoBoxesIsEmpty() => Assert.True(new BoxesByCell<string>([]).IsEmpty);

    /// <summary>A square box of the half size around (x, x), seen from above.</summary>
    private static Box Square(float centre, float half) => new(new Vector3(centre - half, centre - half, 0), new Vector3(centre + half, centre + half, 10));
}
