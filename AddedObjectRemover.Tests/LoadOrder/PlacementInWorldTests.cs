using System.Numerics;

namespace AddedObjectRemover.Tests.LoadOrder;

public class PlacementInWorldTests
{
    [Theory]
    [InlineData(false, false, false, nameof(Presence.Present))]
    [InlineData(true, false, false, nameof(Presence.Hidden))]
    [InlineData(true, true, false, nameof(Presence.Present))]
    [InlineData(true, false, true, nameof(Presence.Hidden))]
    [InlineData(true, true, true, nameof(Presence.Hidden))]
    public void InitiallyDisabledHidesAllButOtherModsObjectsWithAnEnableParent(bool disabled, bool hasEnableParent, bool isTarget, string expected)
    {
        var facts = new PlacementFacts(disabled, hasEnableParent, Vector3.Zero, Vector3.Zero);

        Assert.Equal(Enum.Parse<Presence>(expected), PresenceRule.Of(facts, isTarget));
    }

    [Fact]
    public void RecordWithoutPlacementIsHidden() =>
        Assert.Equal(Presence.Hidden, PresenceRule.Of(new PlacementFacts(false, false, null, null), isWinningTarget: false));

    [Theory]
    [InlineData(0f, 0f, nameof(Presence.Present))]
    [InlineData(float.NaN, 0f, nameof(Presence.InvalidPlacement))]
    [InlineData(float.PositiveInfinity, 0f, nameof(Presence.InvalidPlacement))]
    [InlineData(0f, float.NaN, nameof(Presence.InvalidPlacement))]
    [InlineData(0f, 2e6f, nameof(Presence.InvalidPlacement))]
    public void PlacementNeedsAPositionInRangeAndAFiniteRotation(float rotationX, float positionX, string expected)
    {
        var facts = new PlacementFacts(false, false, new Vector3(positionX, 0, 0), new Vector3(rotationX, 0, 0));

        Assert.Equal(Enum.Parse<Presence>(expected), PresenceRule.Of(facts, isWinningTarget: false));
    }
}
