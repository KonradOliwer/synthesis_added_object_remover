using System.Diagnostics;
using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>The refusals the too-close guards give, and the reading of a too-close check that decides which one.</summary>
internal static class TooCloseRefusals
{
    /// <returns>The clash the too-close rule found; null when it found none or stopped, which the check's failure tells.</returns>
    public static TooClose? ClashOf(PlacementCheck check) =>
        check.Clash switch
        {
            null => null,
            TooClose clash => clash,
            var other => throw new UnreachableException($"The too-close rule gave the reason {other}."),
        };

    public static PlacedRefusal Stopped(string failure) => new(PlacedGuard.TooCloseToWhatStays, By: null, failure);

    /// <param name="standing">The object that stays and that the candidate would come too close to or be too close to.</param>
    public static PlacedRefusal ByWhatStays(RecordKey standing) => new(PlacedGuard.TooCloseToWhatStays, standing, Failure: null);
}
