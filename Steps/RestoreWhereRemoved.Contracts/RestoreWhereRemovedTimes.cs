namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <param name="PlansMade">Judging every candidate's areas and making the terrain, navmesh and water plans.</param>
/// <param name="WorldAfterRemoval">Building the world after the removals, which the guards judge in.</param>
/// <param name="GuardsSetUp">Setting up the objects that stay and the guards over that world, with the first collision layer read.</param>
/// <param name="Passes">All passes of the loop.</param>
/// <param name="PlacedGuardsInPasses">The checks of the placed guards, summed over the passes; part of <paramref name="Passes"/>.</param>
public sealed record RestoreWhereRemovedTimes(
    TimeSpan PlansMade, TimeSpan WorldAfterRemoval, TimeSpan GuardsSetUp, TimeSpan Passes, TimeSpan PlacedGuardsInPasses)
{
    public static readonly RestoreWhereRemovedTimes None = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
}
