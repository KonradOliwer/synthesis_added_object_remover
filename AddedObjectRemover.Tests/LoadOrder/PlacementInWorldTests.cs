using System.Numerics;

namespace AddedObjectRemover.Tests.LoadOrder;

public class PlacementInWorldTests
{
    [Theory]
    [InlineData(false, false, false, nameof(ShownInGame.Present))]
    [InlineData(true, false, false, nameof(ShownInGame.Hidden))]
    [InlineData(true, true, false, nameof(ShownInGame.Present))]
    [InlineData(true, false, true, nameof(ShownInGame.Hidden))]
    [InlineData(true, true, true, nameof(ShownInGame.Hidden))]
    public void InitiallyDisabledHidesAllButOtherModsObjectsWithAnEnableParent(bool disabled, bool hasEnableParent, bool isTarget, string expected)
    {
        var facts = new PlacementFacts(disabled, hasEnableParent, Vector3.Zero, Vector3.Zero);

        Assert.Equal(Enum.Parse<ShownInGame>(expected), StartsDisabledRule.Of(facts, isTarget));
    }

    [Fact]
    public void RecordWithoutPlacementIsHidden() =>
        Assert.Equal(ShownInGame.Hidden, StartsDisabledRule.Of(new PlacementFacts(false, false, null, null), isWinningTarget: false));

    [Theory]
    [InlineData(0f, 0f, nameof(ShownInGame.Present))]
    [InlineData(float.NaN, 0f, nameof(ShownInGame.InvalidPlacement))]
    [InlineData(float.PositiveInfinity, 0f, nameof(ShownInGame.InvalidPlacement))]
    [InlineData(0f, float.NaN, nameof(ShownInGame.InvalidPlacement))]
    [InlineData(0f, 2e6f, nameof(ShownInGame.InvalidPlacement))]
    public void PlacementNeedsAPositionInRangeAndAFiniteRotation(float rotationX, float positionX, string expected)
    {
        var facts = new PlacementFacts(false, false, new Vector3(positionX, 0, 0), new Vector3(rotationX, 0, 0));

        Assert.Equal(Enum.Parse<ShownInGame>(expected), StartsDisabledRule.Of(facts, isWinningTarget: false));
    }
}
