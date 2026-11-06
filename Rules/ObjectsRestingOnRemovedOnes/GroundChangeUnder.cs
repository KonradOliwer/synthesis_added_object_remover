using System.Numerics;
using AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes;

/// <summary>
/// How the ground at an object changes between two grounds, measured at the samples of its surface as the support rule takes them, with
/// the touch gap as the tolerance. A sample where either ground has no height is not measured. Thread-safe; results depend only on the
/// inputs.
/// </summary>
internal sealed class GroundChangeUnder(ISupportContacts surfaces, float touchGap)
{
    /// <summary>Whether a sample touching the ground before (within the touch gap above it, or below it) is farther than the touch gap above the ground after.</summary>
    public bool GoesDownUnder(StandingObject standing, ITerrainHeights before, ITerrainHeights after) =>
        AnySampleLeavesTheTouchGap(standing, before, after, heightAbove: (sampleZ, ground) => sampleZ - ground);

    /// <summary>Whether a sample no deeper than the touch gap below the ground before lies deeper than the touch gap below the ground after.</summary>
    public bool RisesInto(StandingObject standing, ITerrainHeights before, ITerrainHeights after) =>
        AnySampleLeavesTheTouchGap(standing, before, after, heightAbove: (sampleZ, ground) => ground - sampleZ);

    /// <param name="heightAbove">How far a sample lies from a ground in the direction measured.</param>
    /// <returns>Whether a sample within the touch gap of the ground before, measured so, lies beyond it from the ground after.</returns>
    private bool AnySampleLeavesTheTouchGap(StandingObject standing, ITerrainHeights before, ITerrainHeights after, Func<float, float, float> heightAbove)
    {
        if (!before.HasTerrain(standing.Space) || !after.HasTerrain(standing.Space)) return false;
        return surfaces.SampleSurface(standing.MeshPath).Points.Select(standing.Transform.ToWorld).Any(point =>
        {
            var place = new Vector2(point.X, point.Y);
            return before.TryGetHeight(standing.Space, place, out var heightBefore)
                && after.TryGetHeight(standing.Space, place, out var heightAfter)
                && heightAbove(point.Z, heightBefore) <= touchGap
                && heightAbove(point.Z, heightAfter) > touchGap;
        });
    }
}
