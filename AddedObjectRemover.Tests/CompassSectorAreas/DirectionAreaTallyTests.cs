namespace AddedObjectRemover.Tests.CompassSectorAreas;

public class DirectionAreaTallyTests
{
    private const int Seed = 4711;
    private const int Trials = 300;
    private const int MostBoxes = 40;
    private const int CategoryCount = 2;
    private const float MostArea = 1000f;
    private const float Area = 10f;

    [Fact]
    public void EachBoxCountsInAllAndInItsCategory()
    {
        var tally = new DirectionAreaTally(CategoryCount);
        tally.Add(DirectionSector.North, Area, category: 0);
        tally.Add(DirectionSector.North, 2 * Area, category: 1);
        tally.Add(DirectionSector.North, 4 * Area, category: null);

        var areas = tally.Build();

        Assert.Equal((7 * Area, 3), (areas.Total(DirectionSector.North), areas.Boxes(DirectionSector.North)));
        Assert.Equal((Area, 1), (areas.Area(0, DirectionSector.North), areas.Boxes(0, DirectionSector.North)));
        Assert.Equal((2 * Area, 1), (areas.Area(1, DirectionSector.North), areas.Boxes(1, DirectionSector.North)));
        Assert.Equal((0f, 0), (areas.Total(DirectionSector.South), areas.Boxes(DirectionSector.South)));
    }

    [Fact]
    public void AFootprintAroundThePointCountsInEveryDirection()
    {
        var tally = new DirectionAreaTally(CategoryCount);
        tally.Add(new SectorFootprint(Area, Sector: null), category: 0);

        var areas = tally.Build();

        Assert.All(CompassDirections.All, sector => Assert.Equal((Area, Area), (areas.Total(sector), areas.Area(0, sector))));
    }

    [Fact]
    public void TheAreasOfGivenBoxesAreThoseOfATallyThatAddedThemInOrder()
    {
        (SectorFootprint Footprint, int? Category)[] boxes =
            [(new SectorFootprint(Area, DirectionSector.North), 0), (new SectorFootprint(2 * Area, Sector: null), null), (new SectorFootprint(4 * Area, DirectionSector.North), 1)];
        var tally = new DirectionAreaTally(CategoryCount);
        foreach (var (footprint, category) in boxes) tally.Add(footprint, category);

        var areas = DirectionAreaTally.Of(CategoryCount, boxes);

        var added = tally.Build();
        Assert.All(CompassDirections.All, sector => Assert.Equal(
            (added.Total(sector), added.Boxes(sector), added.Area(0, sector), added.Area(1, sector)),
            (areas.Total(sector), areas.Boxes(sector), areas.Area(0, sector), areas.Area(1, sector))));
    }

    [Fact]
    public void ATallyHoldingBoxesInACategoryCannotGiveTheAreasWithCategories()
    {
        var tally = new DirectionAreaTally(CategoryCount);
        tally.Add(DirectionSector.North, Area, category: 0);

        Assert.Throws<InvalidOperationException>(() => tally.WithCategories([]));
    }

    [Fact]
    public void AreasWithCategoriesEqualATallyThatAddedEveryBoxBitForBit()
    {
        var random = new Random(Seed);
        for (var trial = 0; trial < Trials; trial++)
        {
            var boxes = Enumerable.Range(0, random.Next(0, MostBoxes)).Select(_ => (Footprint: RandomFootprint(random), Category: RandomCategory(random))).ToArray();

            var inNone = new DirectionAreaTally(CategoryCount);
            foreach (var (footprint, _) in boxes) inNone.Add(footprint, category: null);
            var full = new DirectionAreaTally(CategoryCount);
            foreach (var (footprint, category) in boxes) full.Add(footprint, category);

            Assert.Equal(Fingerprint(full.Build()), Fingerprint(inNone.WithCategories(boxes)));
        }
    }

    [Fact]
    public void WithCategoriesLeavesTheTallyUnchanged()
    {
        var tally = new DirectionAreaTally(CategoryCount);
        tally.Add(DirectionSector.North, Area, category: null);
        var before = Fingerprint(tally.Build());

        var withCategories = tally.WithCategories([(new SectorFootprint(Area, DirectionSector.North), 0)]);

        Assert.Equal(Area, withCategories.Area(0, DirectionSector.North));
        Assert.Equal(before, Fingerprint(tally.Build()));
    }

    [Theory]
    [InlineData(1f, 1f, 2f, true)]
    [InlineData(1f, 0f, 3f, false)]
    [InlineData(0f, 0f, 0f, true)]
    public void CategoriesReachAShareOfTheAreaOrOfTheBoxesWhenTheDirectionHasNoArea(float first, float second, float none, bool reaches)
    {
        var tally = new DirectionAreaTally(CategoryCount);
        tally.Add(DirectionSector.North, first, category: 0);
        tally.Add(DirectionSector.North, second, category: 1);
        tally.Add(DirectionSector.North, none, category: null);

        Assert.Equal(reaches, tally.Build().CategoriesReachShare(DirectionSector.North, [0, 1], percent: 50));
    }

    /// <summary>Some areas are zero, and some footprints count in every direction.</summary>
    private static SectorFootprint RandomFootprint(Random random)
    {
        var area = random.Next(5) == 0 ? 0f : random.NextSingle() * MostArea;
        var sectorOrAround = random.Next(CompassDirections.SectorCount + 1);
        return new SectorFootprint(area, sectorOrAround == CompassDirections.SectorCount ? null : (DirectionSector)sectorOrAround);
    }

    private static int? RandomCategory(Random random)
    {
        var categoryOrNone = random.Next(CategoryCount + 1);
        return categoryOrNone == CategoryCount ? null : categoryOrNone;
    }

    private static string Fingerprint(DirectionAreas areas) =>
        string.Join(
            '|',
            new[] { Bits(areas.TotalAreas), string.Join(',', areas.TotalBoxes) }
                .Concat(areas.CategoryAreas.Select(categoryAreas => Bits(categoryAreas)))
                .Concat(areas.CategoryBoxes.Select(categoryBoxes => string.Join(',', categoryBoxes))));

    private static string Bits(IEnumerable<float> values) => string.Join(',', values.Select(value => BitConverter.SingleToInt32Bits(value).ToString("X8")));
}
