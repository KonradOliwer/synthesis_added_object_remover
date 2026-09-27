using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.LoadOrder;

public class PlacementInWorldTests
{
    private static readonly FormKey Record = new(ModKey.FromNameAndExtension("Other.esp"), 0x900);
    private static readonly FormKey Parent = new(ModKey.FromNameAndExtension("Other.esp"), 0x901);

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, true)]
    public void InitiallyDisabledHidesAllButOtherModsObjectsWithAnEnableParent(bool disabled, bool hasEnableParent, bool isTarget, bool hidden)
    {
        var record = new PlacedObject(Record, SkyrimRelease.SkyrimSE);
        if (disabled) record.MajorRecordFlagsRaw |= (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.InitiallyDisabled;
        if (hasEnableParent)
        {
            record.EnableParent = new EnableParent();
            record.EnableParent.Reference.SetTo(Parent);
        }

        Assert.Equal(hidden, PlacementInWorld.IsHidden(record, isTarget));
    }

    [Theory]
    [InlineData(0f, 0f, true)]
    [InlineData(float.NaN, 0f, false)]
    [InlineData(float.PositiveInfinity, 0f, false)]
    [InlineData(0f, float.NaN, false)]
    [InlineData(0f, 2e6f, false)]
    public void PlacementNeedsAPositionInRangeAndAFiniteRotation(float rotationX, float positionX, bool valid)
    {
        var placement = new Placement
        {
            Position = new P3Float(positionX, 0, 0),
            Rotation = new P3Float(rotationX, 0, 0),
        };

        Assert.Equal(valid, PlacementInWorld.IsValid(placement));
    }
}
