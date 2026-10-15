using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.SelectObjectsThatCanCauseRemovals.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>The other-mod object a target is too close to, and how it was found.</summary>
internal readonly record struct ZoneHit(OtherId Other, TooCloseCause Cause);
