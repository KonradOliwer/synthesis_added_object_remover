using AddedObjectRemover.Steps.BuildLogAndReports;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

public sealed class TargetOriginsTests
{
    private static readonly PluginName Target = new("Target.esp");

    private static List<TargetObject> Targets(params TargetOrigin[] origins) =>
        [.. origins.Select((origin, index) => TestTargets.Create(index, TestTargets.At(default)) with { Origin = origin })];

    [Fact]
    public void OwnObjectsAreCountedWithoutTheOtherOrigins()
    {
        var targets = Targets(TargetOrigin.CleanedMod, TargetOrigin.Moved, TargetOrigin.CleanedMod);

        Assert.Equal(2, TargetOrigins.CountOwn(targets));
        Assert.Equal(["1 objects of other mods that Target.esp moved"], TargetOrigins.CountedOtherOrigins(targets, Target));
    }

    [Fact]
    public void EveryOtherOriginWithObjectsIsCountedInOriginOrder()
    {
        var targets = Targets(TargetOrigin.Replaced, TargetOrigin.Moved, TargetOrigin.Replaced, TargetOrigin.Moved, TargetOrigin.Moved);

        Assert.Equal(
            ["3 objects of other mods that Target.esp moved", "2 objects of other mods that Target.esp gave another model"],
            TargetOrigins.CountedOtherOrigins(targets, Target));
    }

    [Fact]
    public void AReplacedObjectIsWordedAsGivenAnotherModel() =>
        Assert.Equal("objects of other mods that Target.esp gave another model", TargetOrigins.Phrase(TargetOrigin.Replaced, Target));

    [Fact]
    public void AnOriginWithoutObjectsIsNotCounted() =>
        Assert.Empty(TargetOrigins.CountedOtherOrigins(Targets(TargetOrigin.CleanedMod), Target));

    [Fact]
    public void TheChangedHeadingStartsWithACapital() =>
        Assert.Equal("Objects of other mods that Target.esp changed", TargetOrigins.ChangedHeading(Target));

    [Fact]
    public void EveryOriginHasAPhraseAndAKindName() =>
        Assert.All(
            Enum.GetValues<TargetOrigin>(),
            origin => Assert.False(string.IsNullOrEmpty(TargetOrigins.Phrase(origin, Target)) || string.IsNullOrEmpty(TargetOrigins.KindName(origin))));

    [Fact]
    public void TheKindsOfTheOtherOriginsAreCountedInOriginOrderAndLeaveOutAKindWithoutObjects()
    {
        var both = Targets(TargetOrigin.Replaced, TargetOrigin.Moved, TargetOrigin.CleanedMod, TargetOrigin.Moved);
        var onlyReplaced = Targets(TargetOrigin.CleanedMod, TargetOrigin.Replaced);

        Assert.Equal("2 moved, 1 replaced", TargetOrigins.CountedKinds(both));
        Assert.Equal("1 replaced", TargetOrigins.CountedKinds(onlyReplaced));
    }

    [Fact]
    public void TheKindNamesAreOwnMovedAndReplaced() =>
        Assert.Equal(["own", "moved", "replaced"], Enum.GetValues<TargetOrigin>().Select(TargetOrigins.KindName));
}
