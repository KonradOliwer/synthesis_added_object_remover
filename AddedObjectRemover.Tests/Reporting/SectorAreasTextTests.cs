namespace AddedObjectRemover.Tests.Reporting;

public sealed class SectorAreasTextTests
{
    private const int HalfThreshold = 50;

    [Fact]
    public void AnEmptyDirectionIsADashAndADirectionWithRestoredBoxesNamesTheirAreaApart()
    {
        var tally = new SectorAreaTally(HalfThreshold);
        tally.Add(DirectionSector.East, 100f, BoxFate.Removed);
        tally.Add(DirectionSector.East, 20f, BoxFate.Restored);
        tally.Add(DirectionSector.East, 280f, BoxFate.Stands);
        tally.Add(DirectionSector.West, 40f, BoxFate.Removed);

        Assert.Equal(
            "East 100 (+20 restored)/400, NorthEast -, North -, NorthWest -, West 40/40, SouthWest -, South -, SouthEast -",
            SectorAreasText.Describe(tally.Build()));
    }
}
