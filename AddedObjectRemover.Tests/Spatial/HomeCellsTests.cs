using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Spatial;

public class HomeCellsTests
{
    private static readonly float Cell = ExteriorGrid.CellSize;
    private static readonly CellFact SomeCell = new(new FormKey(TestTargets.TargetMod, 0x300).ToRecordKey(), EditorId: null);

    [Fact]
    public void InteriorTargetHasNoHomeCell()
    {
        var target = Place(cell: null, new Vector3(Cell * 3, 0, 0));

        Assert.Null(HomeCells.Find(target, new GridPlugin(new CellGridPoint(9, 9))));
    }

    [Fact]
    public void TargetInACellWithAGridHasThatCell()
    {
        var target = Place(SomeCell, new Vector3(Cell * 3, 0, 0));

        Assert.Equal(CellArea.Single(-2, 5), HomeCells.Find(target, new GridPlugin(new CellGridPoint(-2, 5))));
    }

    [Fact]
    public void TargetInACellWithoutAGridHasTheCellOfItsPosition()
    {
        var target = Place(SomeCell, new Vector3(Cell * 3 + 1, -Cell / 2, 0));

        Assert.Equal(CellArea.Single(3, -1), HomeCells.Find(target, new GridPlugin(null)));
    }

    private static TargetObject Place(CellFact? cell, Vector3 position) =>
        TestTargets.Create(0, TestTargets.At(position)) with { Cell = cell };

    private sealed class GridPlugin(CellGridPoint? grid) : IPluginRecords
    {
        public CellGridPoint? ReadHomeCellGrid(RecordKey record) => grid;

        public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope) => throw new NotSupportedException();

        public ImmutableArray<LinkFact> ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets) => throw new NotSupportedException();

        public bool HasEnableParent(RecordKey record) => throw new NotSupportedException();

        public void Write(IReadOnlyList<WriteOrder> orders) => throw new NotSupportedException();

        public float[]? ReadTerrain(ExteriorCell cell) => throw new NotSupportedException();

        public RecordKey? FindLandWorldspace(RecordKey space) => throw new NotSupportedException();

        public MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

        public NpcTraits? NpcTraitsOf(BaseKey npc) => throw new NotSupportedException();

        public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public bool TryParsePluginName(string text, out PluginName name) => throw new NotSupportedException();
    }
}
