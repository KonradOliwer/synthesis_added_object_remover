using System.Numerics;

namespace AddedObjectRemover.Tests.BoxSpatialIndex;

public class ItemsBySpaceTests
{
    private static readonly RecordKey SpaceA = new(new PluginName("A.esp"), 1);
    private static readonly RecordKey SpaceB = new(new PluginName("A.esp"), 2);
    private static readonly RecordKey SpaceWithoutItems = new(new PluginName("A.esp"), 3);

    private sealed record Thing(string Name, RecordKey Space, Box Box, bool Counts = true);

    private static Box Around(float x, float half = 10f) => new(new Vector3(x - half, -half, -half), new Vector3(x + half, half, half));

    private static ItemsBySpace<Thing> Create(IReadOnlyList<Thing> things, int threads = 4, Action<Thing>? onMeasure = null) =>
        ItemsBySpace<Thing>.Create(
            things,
            thing => thing.Space,
            thing =>
            {
                onMeasure?.Invoke(thing);
                return thing.Box;
            },
            thing => thing.Counts,
            EqualityComparer<RecordKey>.Default,
            new Execution(threads));

    private static List<int> Overlapping(ItemsInSpace<Thing> space, Box area)
    {
        var slots = new List<int>();
        space.Overlapping(area, _ => true, new SpatialQueryScratch(), slots);
        return slots;
    }

    [Fact]
    public void ItemsKeepTheirInputOrderInsideTheirSpace()
    {
        var bySpace = Create([
            new Thing("a1", SpaceA, Around(0)),
            new Thing("b1", SpaceB, Around(0)),
            new Thing("a2", SpaceA, Around(100)),
            new Thing("b2", SpaceB, Around(100)),
            new Thing("a3", SpaceA, Around(200)),
        ]);

        Assert.Equal(new[] { "a1", "a2", "a3" }, bySpace.In(SpaceA).Items.Select(thing => thing.Name));
        Assert.Equal(new[] { "b1", "b2" }, bySpace.In(SpaceB).Items.Select(thing => thing.Name));
    }

    [Fact]
    public void SpacesAreListedInTheOrderTheyFirstAppear()
    {
        var bySpace = Create([new Thing("b", SpaceB, Around(0)), new Thing("a", SpaceA, Around(0)), new Thing("b2", SpaceB, Around(0))]);

        Assert.Equal(new[] { SpaceB, SpaceA }, bySpace.Spaces.Select(space => space.Space));
    }

    [Fact]
    public void ItemsTheIncludeRuleRejectsAreInNoSpace()
    {
        var bySpace = Create([
            new Thing("kept1", SpaceA, Around(0)),
            new Thing("dropped", SpaceA, Around(0), Counts: false),
            new Thing("kept2", SpaceA, Around(0)),
            new Thing("alone", SpaceB, Around(0), Counts: false),
        ]);

        Assert.Equal(new[] { "kept1", "kept2" }, bySpace.In(SpaceA).Items.Select(thing => thing.Name));
        Assert.Empty(bySpace.In(SpaceB).Items);
        Assert.Single(bySpace.Spaces);
    }

    [Fact]
    public void ASpaceWithoutItemsIsEmptyAndFindsNothing()
    {
        var empty = Create([new Thing("a", SpaceA, Around(0))]).In(SpaceWithoutItems);

        Assert.Empty(empty.Items);
        Assert.Empty(Overlapping(empty, Around(0, half: 1000)));
        Assert.Equal(-1, empty.FirstContaining(Vector3.Zero, _ => true, _ => true, new SpatialQueryScratch()));
        Assert.Equal(0, empty.LargeItemCount);
    }

    [Fact]
    public void BoxesAreMeasuredOnlyWhenTheirSpaceIsQueriedByPosition()
    {
        var measured = new List<string>();
        var bySpace = Create(
            [new Thing("a", SpaceA, Around(0)), new Thing("b", SpaceB, Around(0))],
            threads: 1,
            onMeasure: thing => measured.Add(thing.Name));

        Assert.Empty(measured);
        _ = bySpace.In(SpaceA).Items;
        Assert.Empty(measured);

        Overlapping(bySpace.In(SpaceA), Around(0));
        Assert.Equal(new[] { "a" }, measured);
    }

    [Fact]
    public void OverlappingFindsItemsByTheirBoxNotByWhereTheyAreInTheList()
    {
        var space = Create([
            new Thing("near", SpaceA, Around(0)),
            new Thing("far", SpaceA, Around(5000)),
            new Thing("touching", SpaceA, Around(30)),
        ]).In(SpaceA);

        Assert.Equal(new[] { 0, 2 }, Overlapping(space, Around(15, half: 5)));
        Assert.Equal(new[] { 1 }, Overlapping(space, Around(5000, half: 1)));
        Assert.Empty(Overlapping(space, Around(2500, half: 1)));
    }

    [Fact]
    public void AnItemCoveringManyGridCellsIsFoundOnceAndOnlyWhereItReaches()
    {
        var wide = new Box(new Vector3(-20000, -20000, 0), new Vector3(20000, 20000, 100));
        var space = Create([
            new Thing("wide", SpaceA, wide),
            new Thing("crate", SpaceA, Around(50000)),
            new Thing("spread", SpaceA, new Box(new Vector3(0, 0, 0), new Vector3(1500, 1500, 10))),
        ]).In(SpaceA);

        Assert.Equal(1, space.LargeItemCount);
        Assert.Equal(new[] { 0 }, Overlapping(space, new Box(new Vector3(19000, 0, 50), new Vector3(19000, 0, 50))));
        Assert.Equal(new[] { 1 }, Overlapping(space, new Box(new Vector3(50000, 0, 0), new Vector3(50000, 0, 0))));
        Assert.Equal(new[] { 0, 2 }, Overlapping(space, new Box(new Vector3(0, 0, 5), new Vector3(1500, 1500, 5))));
        Assert.Empty(Overlapping(space, new Box(new Vector3(0, 0, 500), new Vector3(0, 0, 500))));
    }

    [Fact]
    public void FirstContainingReturnsTheLowestIncludedSlotThatContainsThePoint()
    {
        var space = Create([
            new Thing("skipped", SpaceA, Around(0)),
            new Thing("misses", SpaceA, Around(0)),
            new Thing("hit", SpaceA, Around(0)),
            new Thing("later", SpaceA, Around(0)),
        ]).In(SpaceA);

        var found = space.FirstContaining(
            Vector3.Zero,
            include: slot => slot != 0,
            contains: slot => slot >= 2,
            new SpatialQueryScratch());

        Assert.Equal(2, found);
    }

    [Fact]
    public void FirstContainingRunsTheExactTestOnlyForIncludedItemsWhoseBoxHoldsThePoint()
    {
        var space = Create([
            new Thing("far", SpaceA, Around(1000)),
            new Thing("excluded", SpaceA, Around(0)),
            new Thing("near", SpaceA, Around(0)),
        ]).In(SpaceA);
        var tested = new List<int>();

        var found = space.FirstContaining(
            Vector3.Zero,
            include: slot => slot != 1,
            contains: slot =>
            {
                tested.Add(slot);
                return false;
            },
            new SpatialQueryScratch());

        Assert.Equal(-1, found);
        Assert.Equal(new[] { 2 }, tested);
    }

    [Fact]
    public void AnswersDoNotDependOnTheNumberOfThreads()
    {
        var things = Enumerable.Range(0, 200)
            .Select(i => new Thing("t" + i, i % 2 == 0 ? SpaceA : SpaceB, Around(i * 40f, half: 25f)))
            .ToList();
        var area = Around(2000, half: 300);

        var single = Create(things, threads: 1).In(SpaceA);
        var many = Create(things, threads: 8).In(SpaceA);

        Assert.Equal(Overlapping(single, area), Overlapping(many, area));
        Assert.NotEmpty(Overlapping(single, area));
    }
}
