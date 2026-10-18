using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>How a target object's hold and place are found from its cell, the nearest cell with a Location, or its grid position.</summary>
public sealed class TargetAreaNamesTests
{
    private const float CellSize = ExteriorGrid.CellSize;

    private static readonly SpaceFact World = new(TestTargets.Space, "Tamriel", SpaceKind.Worldspace, "Skyrim");
    private static readonly RecordKey PersistentCell = TestTargets.SpaceKey(0x200);
    private static readonly LocationNames Farm = new("The Pale", "Loreius Farm");
    private static readonly LocationNames Camp = new("Whiterun", "Halted Stream Camp");

    [Fact]
    public void ACellWithALocationIsNamedByIt()
    {
        var names = TargetAreaNames.Of(Cells((Key(1), Exterior(0, 0, location: Farm))));

        Assert.Equal(new TargetArea("The Pale", "Loreius Farm", PlaceSource.OwnLocation, new CellGridPoint(0, 0)), names.Name(World, Key(1), Vector3.Zero));
    }

    [Fact]
    public void ANamedCellWithoutALocationKeepsItsNameAndTakesTheNearbyHold()
    {
        var names = TargetAreaNames.Of(Cells((Key(1), Exterior(0, 0, name: "Riverside")), (Key(2), Exterior(1, 0, location: Farm))));

        Assert.Equal(new TargetArea("The Pale", "Riverside", PlaceSource.CellName, new CellGridPoint(0, 0)), names.Name(World, Key(1), Vector3.Zero));
    }

    [Fact]
    public void AnUnnamedCellBorrowsTheNearestLocationWithinThreeCells()
    {
        var names = TargetAreaNames.Of(Cells(
            (Key(1), Exterior(0, 0)),
            (Key(2), Exterior(3, 0, location: Farm)),
            (Key(3), Exterior(2, 1, location: Camp))));

        Assert.Equal(new TargetArea("Whiterun", "Halted Stream Camp", PlaceSource.NearbyLocation, new CellGridPoint(0, 0)), names.Name(World, Key(1), Vector3.Zero));
    }

    [Fact]
    public void OfEquallyNearLocationsTheCellFirstInKeyOrderWins()
    {
        var names = TargetAreaNames.Of(Cells(
            (Key(1), Exterior(0, 0)),
            (Key(3), Exterior(-1, 0, location: Farm)),
            (Key(2), Exterior(0, 1, location: Camp))));

        Assert.Equal("Halted Stream Camp", names.Name(World, Key(1), Vector3.Zero).Place);
    }

    [Fact]
    public void ALocationMoreThanThreeCellsAwayIsNotBorrowed()
    {
        var names = TargetAreaNames.Of(Cells((Key(1), Exterior(0, 0)), (Key(2), Exterior(4, 0, location: Farm))));

        Assert.Equal(new TargetArea(null, null, PlaceSource.GridOnly, new CellGridPoint(0, 0)), names.Name(World, Key(1), Vector3.Zero));
    }

    [Fact]
    public void AnObjectInThePersistentCellIsNamedByTheGridCellOfItsPosition()
    {
        var names = TargetAreaNames.Of(Cells((PersistentCell, new CellAreaFact(World.Key, Grid: null, Name: null, Location: null)), (Key(1), Exterior(2, -1, location: Farm))));

        var area = names.Name(World, PersistentCell, new Vector3(2.5f * CellSize, -0.5f * CellSize, 0));

        Assert.Equal(new TargetArea("The Pale", "Loreius Farm", PlaceSource.OwnLocation, new CellGridPoint(2, -1)), area);
    }

    [Fact]
    public void APersistentObjectWhereNoCellIsBorrowsTheNearestLocation()
    {
        var names = TargetAreaNames.Of(Cells((PersistentCell, new CellAreaFact(World.Key, Grid: null, Name: null, Location: null)), (Key(1), Exterior(1, 1, location: Farm))));

        Assert.Equal(PlaceSource.NearbyLocation, names.Name(World, PersistentCell, Vector3.Zero).PlaceSource);
    }

    [Fact]
    public void AnInteriorIsNamedByItsCellAndTheHoldOfItsLocation()
    {
        var interior = TestTargets.SpaceKey(0x300);
        var space = new SpaceFact(interior, "Inn", SpaceKind.Interior, "Bannered Mare");
        var names = TargetAreaNames.Of(Cells((interior, new CellAreaFact(interior, Grid: null, "Bannered Mare", new LocationNames("Whiterun", "Whiterun City")))));

        Assert.Equal(new TargetArea("Whiterun", "Bannered Mare", PlaceSource.CellName, Grid: null), names.Name(space, interior, Vector3.Zero));
    }

    [Fact]
    public void CellsOfAnotherWorldspaceAreNeverBorrowed()
    {
        var other = TestTargets.SpaceKey(0x400);
        var names = TargetAreaNames.Of(Cells((Key(1), Exterior(0, 0)), (Key(2), new CellAreaFact(other, new CellGridPoint(1, 0), Name: null, Farm))));

        Assert.Equal(PlaceSource.GridOnly, names.Name(World, Key(1), Vector3.Zero).PlaceSource);
    }

    private static RecordKey Key(uint id) => TestTargets.SpaceKey(0x500 + id);

    private static CellAreaFact Exterior(int x, int y, string? name = null, LocationNames? location = null) =>
        new(World.Key, new CellGridPoint(x, y), name, location);

    private static Dictionary<RecordKey, CellAreaFact> Cells(params (RecordKey Key, CellAreaFact Cell)[] cells) =>
        cells.ToDictionary(cell => cell.Key, cell => cell.Cell);
}
