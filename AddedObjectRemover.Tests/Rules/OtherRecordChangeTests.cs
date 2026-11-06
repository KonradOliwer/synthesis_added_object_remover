using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Rules.ObjectsThatCanCauseRemovals;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// Another mod's placed record whose position an earlier run gave back from an untrusted plugin: the patch's position is accepted for
/// that part only, and only while the patch still holds the given-back value.
/// </summary>
public sealed class OtherRecordChangeTests
{
    private static readonly PluginName BaseGame = new("Skyrim.esm");
    private static readonly PluginName MovingMod = new("MovingMod.esp");
    private static readonly PluginName EarlierCleaned = new("EarlierCleaned.esp");
    private static readonly PluginName Patch = TestTargets.PatchName;
    private static readonly RecordKey Rock = new(BaseGame, 0x800);
    private static readonly Vector3 MovedPlace = new(50, 0, 0);

    private static readonly AcceptedWorld World = new(
        Patch,
        ImmutableDictionary<PluginName, VersionTrust>.Empty
            .Add(BaseGame, VersionTrust.Full)
            .Add(MovingMod, VersionTrust.None)
            .Add(EarlierCleaned, VersionTrust.None));

    private static readonly RestoredPart PositionGivenBack = new(EarlierCleaned, Rock, new RestoredPartName.Placed(PlacedParts.Position), MovingMod);

    [Fact]
    public void APositionThePatchWasGivenBackFromAnUntrustedPluginIsNoChange() =>
        Assert.Null(RemovalCauses.CreateOtherRecordChange([PositionGivenBack]).Decide(RecordWithPatchAt(MovedPlace), World));

    [Fact]
    public void WithoutTheGivenBackPartThePluginTheValueCameFromIsNamed() =>
        Assert.Equal(MovingMod, RemovalCauses.CreateOtherRecordChange([]).Decide(RecordWithPatchAt(MovedPlace), World)!.Value.ChangingMod);

    [Fact]
    public void APatchThatNoLongerHoldsTheGivenBackPositionStaysChanged() =>
        Assert.NotNull(RemovalCauses.CreateOtherRecordChange([PositionGivenBack]).Decide(RecordWithPatchAt(new Vector3(500, 0, 0)), World));

    [Fact]
    public void AGivenBackPartOfAnotherPropertyAcceptsNothingHere()
    {
        var rotation = PositionGivenBack with { Part = new RestoredPartName.Placed(PlacedParts.Rotation) };

        Assert.NotNull(RemovalCauses.CreateOtherRecordChange([rotation]).Decide(RecordWithPatchAt(MovedPlace), World));
    }

    /// <summary>The base game places the rock at the origin, the moving mod moves it, the earlier cleaned mod moves it again, and the patch holds the given position.</summary>
    private static PlacedRecordFact RecordWithPatchAt(Vector3 patchPlace)
    {
        var record = TestPlacedRecords.Create(Rock, BaseGame, Vector3.Zero);
        return record with
        {
            Versions =
            [
                record.Winner,
                record.Winner with { Plugin = MovingMod, Placement = record.Placement with { Position = MovedPlace } },
                record.Winner with { Plugin = EarlierCleaned, Placement = record.Placement with { Position = new Vector3(900, 0, 0) } },
                record.Winner with { Plugin = Patch, Placement = record.Placement with { Position = patchPlace } },
            ],
        };
    }
}
