namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <param name="PlansMade">Judging every candidate's areas and making the terrain, navmesh and water plans.</param>
/// <param name="WorldAfterRemoval">Building the world after the removals, which the guards judge in.</param>
/// <param name="GuardsSetUp">Setting up the objects that stay and the guards over that world, with the first collision layer read.</param>
/// <param name="Passes">All passes of the loop.</param>
/// <param name="InPasses">The parts of the passes, each summed over the passes; parts of <paramref name="Passes"/>.</param>
public sealed record RestoreWhereRemovedTimes(
    TimeSpan PlansMade, TimeSpan WorldAfterRemoval, TimeSpan GuardsSetUp, TimeSpan Passes, RestorePassTimes InPasses)
{
    public static readonly RestoreWhereRemovedTimes None = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, RestorePassTimes.None);
}

/// <param name="PlansRead">
/// Reading what the state before a pass plans: the placed records going back, the ground and water the plans leave, and the navmeshes.
/// </param>
/// <param name="FollowUp">The follow-up of the placed records going back and of the ground going down.</param>
/// <param name="WhatStands">Reading what stands in the final world, given which placed records go back.</param>
/// <param name="PlacedGuards">The checks of the placed guards and the too-close check of the cleaned mod's objects that stand.</param>
/// <param name="ChangedTerrain">The changed-terrain check of the objects that stand.</param>
/// <param name="NavmeshGuards">The guards of the navmesh groups.</param>
/// <param name="GroundProtected">Finding the grid points and quarters the terrain plan keeps.</param>
/// <param name="WaterKept">Finding the water levels the water plan keeps.</param>
public sealed record RestorePassTimes(
    TimeSpan PlansRead,
    TimeSpan FollowUp,
    TimeSpan WhatStands,
    TimeSpan PlacedGuards,
    TimeSpan ChangedTerrain,
    TimeSpan NavmeshGuards,
    TimeSpan GroundProtected,
    TimeSpan WaterKept)
{
    public static readonly RestorePassTimes None = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

    public RestorePassTimes Plus(RestorePassTimes other) =>
        new(
            PlansRead + other.PlansRead,
            FollowUp + other.FollowUp,
            WhatStands + other.WhatStands,
            PlacedGuards + other.PlacedGuards,
            ChangedTerrain + other.ChangedTerrain,
            NavmeshGuards + other.NavmeshGuards,
            GroundProtected + other.GroundProtected,
            WaterKept + other.WaterKept);
}
