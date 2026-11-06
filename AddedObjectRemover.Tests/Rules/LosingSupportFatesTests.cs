using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The one fate of a pulled object that loses its support, shared by the removal steps and the restore's arrivals: one that would
/// move down no farther than the touch gap still rests where it stands and is left there, so nothing is written for it.
/// </summary>
public sealed class LosingSupportFatesTests
{
    private const float TouchGap = 1f;

    [Theory]
    [InlineData(0.5f, OtherModsObjectFate.LeftStillTouching)]
    [InlineData(5f, OtherModsObjectFate.PulledDown)]
    [InlineData(null, OtherModsObjectFate.RemovedNothingBelow)]
    public void APulledObjectIsLeftWhereItStillRests(float? drop, OtherModsObjectFate fate)
    {
        var lantern = TestShapes.Placed(ModKey.FromNameAndExtension("Base.esm"), 1, default, new Vector3(0, 0, 50));
        var fates = new LosingSupportFates(
            rules: null!,
            new DropFinder(surfaces: null!, cache: null!, TouchGap),
            new OtherModsObjectsSettings(OtherModsObjectsHandling.PullObjectToTheGround, TouchGap, SupportLostFraction: 0.5f));

        Assert.Equal(fate, fates.FateOfPull(lantern, lostShare: 1f, drop).Fate);
    }
}
