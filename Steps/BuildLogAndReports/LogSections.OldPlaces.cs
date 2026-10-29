using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>How many old places the judge step checked and what it found at them; the detailed log lists each collision after it.</summary>
    /// <param name="judged">A result with at least one old-place check.</param>
    /// <param name="oldPlaceWorld">The objects the checks were made among, which the collisions refer to.</param>
    public static LogSection BuildOldPlaceChecksSection(
        CollectedObjects world, ChangedObjectJudgingResult judged, CollectedObjects? oldPlaceWorld, PluginName target, TimeSpan elapsed, ReportContext context)
    {
        var checks = judged.OldPlaceChecks;
        var failed = checks.Count(check => check.Failure is not null);
        var colliding = checks.Where(check => check.Collision is not null).ToList();
        var counts = $"{TextFormat.Count(checks.Length - failed - colliding.Count)} free, {TextFormat.Count(colliding.Count)} colliding"
            + (failed > 0 ? $", {TextFormat.Count(failed)} failed" : string.Empty);
        return new LogSection(
            "old-places",
            [
                $"Checked the old places of {TextFormat.Count(checks.Length)} {TargetOrigins.ChangedPhrase(target)}: {counts}{Describe.TimedSuffix(elapsed, context)}.",
                .. context.Detailed ? colliding.Select(check => CollisionLine(world, check, oldPlaceWorld ?? throw new InvalidOperationException("An old-place check found a collision, but no world of the checks was given."))) : [],
            ]);
    }

    private static string CollisionLine(CollectedObjects world, OldPlaceCheck check, CollectedObjects oldPlaceWorld)
    {
        var checkedObject = world.Targets[check.Target.Index];
        var reason = Describe.CollisionReason(RemovalList.Collision(check.Target, check.Collision!, oldPlaceWorld));
        return $"  Collides at its old place: {RecordNames.Describe(checkedObject)} in {Describe.Space(world, checkedObject.SpaceKey)}; {reason}.";
    }
}
