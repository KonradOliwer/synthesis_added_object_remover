using AddedObjectRemover.Tests.EndToEnd;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>A cell copy's water fields by the game's rules, and the copies of a cell in load order.</summary>
public class CellWaterTests
{
    private const float CellHeight = -50f;
    private const float WorldspaceDefault = 20f;

    private static readonly RecordKey Worldspace = TestTargets.SpaceKey(0x200);
    private static readonly ExteriorCell WaterCell = new(Worldspace, 0, 0);
    private static readonly PluginName Base = new("Base.esm");
    private static readonly PluginName Early = new("Early.esp");
    private static readonly PluginName Late = new("Late.esp");
    private static readonly WorldspaceWaterCopy WithDefault = new(Base, WorldspaceDefault);

    [Fact]
    public void ACellWithoutTheHasWaterFlagIsReadAsWithoutWaterWithTheHeightItStores() =>
        Assert.Equal(new CellWaterCopy(Base, HasWater: false, CellHeight), CopyOf(Cell.Flag.ShowSky, CellHeight));

    [Fact]
    public void ACellWithWaterKeepsItsOwnHeight() =>
        Assert.Equal(new CellWaterCopy(Base, HasWater: true, CellHeight), CopyOf(Cell.Flag.HasWater, CellHeight));

    [Fact]
    public void ACellWithWaterAndNoHeightUsesTheWorldspaceDefault() =>
        Assert.Null(CopyOf(Cell.Flag.HasWater, height: null).OwnLevel);

    [Fact]
    public void ACellStoringTheLargestFloatUsesTheWorldspaceDefault() =>
        Assert.Null(CopyOf(Cell.Flag.HasWater, float.MaxValue).OwnLevel);

    [Fact]
    public void TheCellCopiesFoundHighestPriorityFirstAreReadInLoadOrderWithTheWorldspaceCopies()
    {
        CellWaterCopy[] cellCopies = [new(Early, true, null), new(Late, true, CellHeight)];
        WorldspaceWaterCopy[] worldspaceCopies = [WithDefault, new(Late, DefaultLevel: null)];

        var copies = Water(cellCopies, worldspaceCopies).ReadCopies(WaterCell);

        Assert.Equal(cellCopies, copies.Cell);
        Assert.Equal(worldspaceCopies, copies.Worldspace);
    }

    [Fact]
    public void ACellWithoutAnyCopyHasNoCellCopies() =>
        Assert.Empty(Water([new(Base, true, CellHeight)], [WithDefault]).ReadCopies(new ExteriorCell(Worldspace, 5, 5)).Cell);

    [Fact]
    public void TheCopiesMustHaveBeenAskedFor() =>
        Assert.Throws<InvalidOperationException>(() => new CellWater(null, new Dictionary<RecordKey, List<WorldspaceWaterCopy>>(), new Dictionary<RecordKey, RecordKey>()).ReadCopies(WaterCell));

    [Fact]
    public void AWorldspaceUsingItsParentsWaterDataReadsTheParentsCopies()
    {
        var child = TestTargets.SpaceKey(0x300);
        var water = new CellWater(
            new Dictionary<ExteriorCell, List<CellWaterCopy>>(),
            new Dictionary<RecordKey, List<WorldspaceWaterCopy>> { [Worldspace] = [WithDefault] },
            new Dictionary<RecordKey, RecordKey> { [child] = Worldspace });

        Assert.Equal([WithDefault], water.ReadCopies(new ExteriorCell(child, 0, 0)).Worldspace);
    }

    private static CellWaterCopy CopyOf(Cell.Flag flags, float? height) =>
        WaterLevel.ReadCopy(Base, new Cell(FormKey.Factory("000800:Base.esm"), FixtureMod.Release) { Flags = flags, WaterHeight = height });

    /// <summary>The copies in the order the reader finds them: the cell copies highest priority first, the worldspace copies in load order.</summary>
    private static CellWater Water(CellWaterCopy[] cellCopies, WorldspaceWaterCopy[] worldspaceCopies) => new(
        new Dictionary<ExteriorCell, List<CellWaterCopy>> { [WaterCell] = [.. cellCopies.Reverse()] },
        new Dictionary<RecordKey, List<WorldspaceWaterCopy>> { [Worldspace] = [.. worldspaceCopies] },
        new Dictionary<RecordKey, RecordKey> { [Worldspace] = Worldspace });
}
