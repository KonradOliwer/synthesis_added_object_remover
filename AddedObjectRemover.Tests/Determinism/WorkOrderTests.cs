using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Determinism;

public class WorkOrderTests
{
    private static readonly FormKey FirstSpace = new(TestTargets.TargetMod, 0x101);
    private static readonly FormKey SecondSpace = new(TestTargets.TargetMod, 0x102);
    private static readonly float Cell = ExteriorGrid.CellSize;

    [Fact]
    public void TargetsAreOrderedBySpaceThenCellThenIndex()
    {
        List<TargetObject> targets =
        [
            Place(0, SecondSpace, new Vector3(0, 0, 0)),
            Place(1, FirstSpace, new Vector3(Cell, 0, 0)),
            Place(2, FirstSpace, new Vector3(0, Cell, 0)),
            Place(3, FirstSpace, new Vector3(-Cell, 0, 0)),
            Place(4, FirstSpace, new Vector3(Cell / 2, Cell / 2, 0)),
            Place(5, FirstSpace, new Vector3(0, 0, 0)),
        ];

        var order = WorkOrder.Of(targets);

        Assert.Equal(new[] { 3, 4, 5, 2, 1, 0 }, order.TargetsBySpaceAndCell.ToArray());
    }

    [Fact]
    public void AmongOrdersPositionsOfASubsetByTheirTargetsRank()
    {
        var order = new WorkOrder([4, 2, 0, 3, 1]);

        Assert.Equal(new[] { 1, 0, 2 }, order.Among([0, 4, 3]).ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, order.Among([2, 2, 1]).ToArray());
        Assert.Empty(order.Among([]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void AmongRejectsATargetTheOrderWasNotBuiltFor(int target)
    {
        var order = new WorkOrder([2, 0, 1]);

        Assert.Throws<ArgumentOutOfRangeException>(() => order.Among([0, target]));
    }

    [Fact]
    public void TargetsInTheSameSpaceAndCellKeepTheirIndexOrder()
    {
        List<TargetObject> targets =
        [
            Place(0, FirstSpace, new Vector3(10, 10, 0)),
            Place(1, FirstSpace, new Vector3(20, 20, 0)),
            Place(2, FirstSpace, new Vector3(30, 30, 0)),
        ];

        Assert.Equal(new[] { 0, 1, 2 }, WorkOrder.Of(targets).TargetsBySpaceAndCell.ToArray());
    }

    [Fact]
    public void NegativeCellsComeFirstAndXComesBeforeY()
    {
        List<TargetObject> targets =
        [
            Place(0, FirstSpace, new Vector3(0, Cell, 0)),
            Place(1, FirstSpace, new Vector3(Cell, -Cell, 0)),
            Place(2, FirstSpace, new Vector3(-Cell, Cell, 0)),
            Place(3, FirstSpace, new Vector3(-1, -1, 0)),
            Place(4, FirstSpace, new Vector3(0, 0, 0)),
        ];

        var order = WorkOrder.Of(targets);

        Assert.Equal(new[] { 3, 2, 4, 0, 1 }, order.TargetsBySpaceAndCell.ToArray());
    }

    [Fact]
    public void AmongOrdersASubsetSpanningSeveralSpaces()
    {
        List<TargetObject> targets =
        [
            Place(0, SecondSpace, new Vector3(0, 0, 0)),
            Place(1, FirstSpace, new Vector3(Cell, 0, 0)),
            Place(2, SecondSpace, new Vector3(-Cell, 0, 0)),
            Place(3, FirstSpace, new Vector3(0, 0, 0)),
        ];

        var order = WorkOrder.Of(targets);

        Assert.Equal(new[] { 3, 1, 2, 0 }, order.TargetsBySpaceAndCell.ToArray());
        Assert.Equal(new[] { 3, 1, 2, 0 }, order.Among([0, 1, 2, 3]).ToArray());
        Assert.Equal(new[] { 2, 1, 0 }, order.Among([0, 1, 3]).ToArray());
    }

    [Fact]
    public void ARequiredLengthThatDiffersFromTheTargetCountThrows()
    {
        var order = new WorkOrder([1, 0]);

        order.RequireCovers(2);
        Assert.Throws<ArgumentException>(() => order.RequireCovers(3));
        Assert.Throws<ArgumentException>(() => order.RequireCovers(1));
    }

    [Fact]
    public void AnOrderThatIsNotAPermutationThrows() =>
        Assert.Throws<ArgumentException>(() => new WorkOrder([0, 0, 1]));

    private static TargetObject Place(int index, FormKey space, Vector3 position) =>
        TestTargets.Create(index, TestTargets.At(position), baseRef: null, space);
}
