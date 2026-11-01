using AddedObjectRemover.Tests.EndToEnd;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.RecordParts;

/// <summary>What Mutagen's translation masks do with the masks the part tables build from field names.</summary>
public class MaskProbeTests
{
    private static readonly FormKey CellKey = FormKey.Factory("000800:Base.esm");

    [Fact]
    public void AMaskWithEveryFieldOnButASubRecordIgnoresTheWholeSubRecord()
    {
        var mask = TranslationMasks.AllBut<Cell.TranslationMask>([nameof(ICellGetter.Lighting)]);
        var lit = new Cell(CellKey, FixtureMod.Release) { Lighting = new CellLighting { FogNear = 10f } };
        var dark = new Cell(CellKey, FixtureMod.Release);

        Assert.True(lit.Equals(dark, mask));
        ((ICellInternal)dark).DeepCopyIn(lit, errorMask: null, copyMask: mask.GetCrystal());
        Assert.Null(dark.Lighting);
    }

    [Fact]
    public void AMaskWithOnlyASubRecordOnComparesAndCopiesItWhole()
    {
        var mask = TranslationMasks.Only<Cell.TranslationMask>([nameof(ICellGetter.Lighting)]);
        var lit = new Cell(CellKey, FixtureMod.Release) { Lighting = new CellLighting { FogNear = 10f }, EditorID = "Lit" };
        var dark = new Cell(CellKey, FixtureMod.Release);

        Assert.False(lit.Equals(dark, mask));
        ((ICellInternal)dark).DeepCopyIn(lit, errorMask: null, copyMask: mask.GetCrystal());
        Assert.Equal(10f, dark.Lighting!.FogNear);
        Assert.Null(dark.EditorID);
    }

    [Fact]
    public void BothViewsOfTheRecordFlagsMustBeOffToIgnoreThem()
    {
        var flagged = new Cell(CellKey, FixtureMod.Release) { MajorFlags = Cell.MajorFlag.OffLimits };
        var plain = new Cell(CellKey, FixtureMod.Release);

        Assert.False(flagged.Equals(plain, TranslationMasks.AllBut<Cell.TranslationMask>([nameof(ICellGetter.MajorRecordFlagsRaw)])));
        Assert.True(flagged.Equals(plain, TranslationMasks.AllBut<Cell.TranslationMask>(RecordHeader.FlagFields)));
    }

    [Fact]
    public void AFieldTheMaskDoesNotHaveIsABug() =>
        Assert.Throws<ArgumentException>(() => TranslationMasks.AllBut<Cell.TranslationMask>(["NoSuchField"]));
}
