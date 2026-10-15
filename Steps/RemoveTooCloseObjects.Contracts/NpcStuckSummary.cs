namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <param name="Sizes">Per placed NPC of the spaces holding a visible target, how its size was found.</param>
/// <param name="CoreTests">Body boxes tested against an object, combined boxes of several possible bodies included.</param>
/// <param name="Conflicts">Target objects found with an NPC stuck in them.</param>
/// <param name="PointFallbacks">NPCs sized as a point, with why, ordered by record key text; for the detailed log only.</param>
public sealed record NpcStuckSummary(NpcSizeCounts Sizes, long PairsTested, long CoreTests, int Conflicts, IReadOnlyList<PointNpc> PointFallbacks);
