using System.Numerics;

namespace AddedObjectRemover.Tests.LoadOrder;

public class PlacementInWorldTests
{
    /// <summary>The enable parent a record has, if any.</summary>
    public enum ParentForm { None, PlayerOpposite, PlayerSameState, OtherOpposite, OtherSameState }

    private static readonly RecordKey OtherParent = new(new PluginName("Other.esp"), 0x801);

    private static readonly PresenceFacts Shown = new(Deleted: false, InitiallyDisabled: false, EnableParent: null);

    [Theory]
    [InlineData(true, false, ParentForm.None)]
    [InlineData(true, true, ParentForm.OtherSameState)]
    [InlineData(false, true, ParentForm.None)]
    [InlineData(false, true, ParentForm.PlayerOpposite)]
    [InlineData(false, false, ParentForm.PlayerOpposite)]
    public void EachRemovedFormIsRemoved(bool deleted, bool initiallyDisabled, ParentForm parent) =>
        Assert.True(RemovedForm.IsRemoved(new PresenceFacts(deleted, initiallyDisabled, EnableParentOf(parent))));

    [Theory]
    [InlineData(false, ParentForm.None)]
    [InlineData(false, ParentForm.PlayerSameState)]
    [InlineData(true, ParentForm.PlayerSameState)]
    [InlineData(false, ParentForm.OtherOpposite)]
    [InlineData(true, ParentForm.OtherOpposite)]
    [InlineData(true, ParentForm.OtherSameState)]
    public void ARecordInNoRemovedFormIsNotRemoved(bool initiallyDisabled, ParentForm parent) =>
        Assert.False(RemovedForm.IsRemoved(new PresenceFacts(Deleted: false, initiallyDisabled, EnableParentOf(parent))));

    [Theory]
    [InlineData(false, false, nameof(ShownInGame.Present))]
    [InlineData(true, false, nameof(ShownInGame.Present))]
    [InlineData(false, true, nameof(ShownInGame.Present))]
    [InlineData(true, true, nameof(ShownInGame.Hidden))]
    public void InitiallyDisabledWithAnEnableParentHidesOnlyAWinningTargetRecord(bool disabled, bool isTarget, string expected)
    {
        var facts = new PlacementFacts(new PresenceFacts(Deleted: false, disabled, EnableParentOf(ParentForm.OtherSameState)), Vector3.Zero, Vector3.Zero);

        Assert.Equal(Enum.Parse<ShownInGame>(expected), StartsDisabledRule.Of(facts, isTarget));
    }

    [Fact]
    public void RecordWithoutPlacementIsHidden() =>
        Assert.Equal(ShownInGame.Hidden, StartsDisabledRule.Of(new PlacementFacts(Shown, null, null), isWinningTarget: false));

    [Theory]
    [InlineData(0f, 0f, nameof(ShownInGame.Present))]
    [InlineData(float.NaN, 0f, nameof(ShownInGame.InvalidPlacement))]
    [InlineData(float.PositiveInfinity, 0f, nameof(ShownInGame.InvalidPlacement))]
    [InlineData(0f, float.NaN, nameof(ShownInGame.InvalidPlacement))]
    [InlineData(0f, 2e6f, nameof(ShownInGame.InvalidPlacement))]
    public void PlacementNeedsAPositionInRangeAndAFiniteRotation(float rotationX, float positionX, string expected)
    {
        var facts = new PlacementFacts(Shown, new Vector3(positionX, 0, 0), new Vector3(rotationX, 0, 0));

        Assert.Equal(Enum.Parse<ShownInGame>(expected), StartsDisabledRule.Of(facts, isWinningTarget: false));
    }

    private static EnableParentFact? EnableParentOf(ParentForm parent) => parent switch
    {
        ParentForm.None => null,
        ParentForm.PlayerOpposite => new EnableParentFact(RemovalConvention.PlayerRef, Opposite: true),
        ParentForm.PlayerSameState => new EnableParentFact(RemovalConvention.PlayerRef, Opposite: false),
        ParentForm.OtherOpposite => new EnableParentFact(OtherParent, Opposite: true),
        ParentForm.OtherSameState => new EnableParentFact(OtherParent, Opposite: false),
        _ => throw new ArgumentOutOfRangeException(nameof(parent), parent, null),
    };
}
