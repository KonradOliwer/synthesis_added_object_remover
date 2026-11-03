using System.Numerics;

namespace AddedObjectRemover.Tests.LoadOrder;

/// <summary>
/// The one comparison of re-saved placed parts: a gap of exactly the tolerance is no change, a gap past it is. Each value is a
/// multiple of its tolerance, so the gaps are exact in floats.
/// </summary>
public class ResavedPlacedPartsTests
{
    private const float Past = 3f;

    [Fact]
    public void APositionChangesOnlyPastItsTolerance()
    {
        var tolerance = ResavedPlacedParts.Tolerances.Position;

        Assert.False(ResavedPlacedParts.PositionsDiffer(new Vector3(tolerance), new Vector3(2 * tolerance)));
        Assert.True(ResavedPlacedParts.PositionsDiffer(new Vector3(tolerance), new Vector3(0, 0, Past * tolerance)));
    }

    [Fact]
    public void ARotationChangesOnlyPastItsToleranceAndNotByAFullTurn()
    {
        var tolerance = ResavedPlacedParts.Tolerances.Rotation;

        Assert.False(ResavedPlacedParts.RotationsDiffer(new Vector3(tolerance), new Vector3(2 * tolerance)));
        Assert.True(ResavedPlacedParts.RotationsDiffer(new Vector3(tolerance), new Vector3(tolerance, Past * tolerance, tolerance)));
        Assert.False(ResavedPlacedParts.RotationsDiffer(Vector3.Zero, new Vector3(0, 0, 2 * MathF.PI)));
    }

    [Fact]
    public void AScaleChangesOnlyPastItsToleranceAndAMissingScaleIsOne()
    {
        var tolerance = ResavedPlacedParts.Tolerances.Scale;

        Assert.False(ResavedPlacedParts.ScalesDiffer(tolerance, 2 * tolerance));
        Assert.True(ResavedPlacedParts.ScalesDiffer(tolerance, Past * tolerance));
        Assert.False(ResavedPlacedParts.ScalesDiffer(null, 1f));
        Assert.True(ResavedPlacedParts.ScalesDiffer(null, 2f));
    }
}
