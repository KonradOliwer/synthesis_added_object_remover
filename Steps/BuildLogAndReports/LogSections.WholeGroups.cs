using System.Collections.Immutable;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveWholeGroups.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>The step's name in the log.</summary>
    public const string WholeGroupsStepName = "Objects in groups with a clash removal";

    private const string WholeGroupsSectionId = "whole-groups";

    /// <summary>Game units are printed whole in the group lines.</summary>
    private const int GroupPlaceDecimals = 0;

    /// <param name="decisions">The decisions with the step's round, which tell what became of each group's seed.</param>
    /// <param name="kept">The objects the step's round held.</param>
    /// <remarks>
    /// Only the groups the step's round took objects from are counted and listed. The counts line comes first and only when the round
    /// removed or kept an object; then the detailed log's line per such group and the kept lines. A stopped step has the
    /// not-performed line alone.
    /// </remarks>
    public static LogSection BuildWholeGroupsSection(
        CollectedObjects world, WholeGroupsResult found, IRemovalDecisions decisions, IEnumerable<KeptObject> kept, TimeSpan elapsed, ReportContext context)
    {
        if (found.Outcome == StepOutcome.StoppedByUserDataFailure) return new(WholeGroupsSectionId, [BuildNotPerformedLine(WholeGroupsStepName)]);

        var removed = found.Evaluations.Where(evaluation => evaluation.IsRemoved).ToList();
        var protectedKinds = found.Evaluations.Count(evaluation => evaluation.Decision == LeftBehindOutcome.KeptProtectedType);
        if (removed.Count == 0 && protectedKinds == 0) return new(WholeGroupsSectionId, []);

        List<SeededGroup> losing = [.. removed.Select(evaluation => evaluation.GroupIndex).Distinct().Order().Select(index => found.Groups[index])];
        return new(
            WholeGroupsSectionId,
            [
                WholeGroupsCountLine(removed.Count, losing, protectedKinds, elapsed, context),
                .. context.Detailed ? losing.Select(group => GroupLine(world, group, decisions)) : [],
                .. BuildKeptLines(world, kept),
            ]);
    }

    /// <summary>"Objects in groups with a clash removal: 412 removed in 3 groups (largest 56); kept: 5 of a kind always kept."</summary>
    /// <param name="losing">The groups the round took objects from.</param>
    private static string WholeGroupsCountLine(int removed, IReadOnlyList<SeededGroup> losing, int protectedKinds, TimeSpan elapsed, ReportContext context)
    {
        var removedPart = losing.Count == 0
            ? "0 removed"
            : $"{TextFormat.Count(removed)} removed in {TextFormat.Count(losing.Count, "group", "groups")} (largest {TextFormat.Count(losing.Max(group => group.Members.Length))})";
        var keptPart = protectedKinds == 0 ? string.Empty : $"; kept: {TextFormat.Count(protectedKinds)} of a kind always kept";
        return $"{WholeGroupsStepName}: {removedPart}{keptPart}{Describe.TimedSuffix(elapsed, context)}.";
    }

    /// <summary>"  Group of 56: seed removed 012345:Mod.esp (Rock01); worldspace Tamriel, from (1200, -3400) to (2600, -2100)."</summary>
    private static string GroupLine(CollectedObjects world, SeededGroup group, IRemovalDecisions decisions) =>
        $"  Group of {TextFormat.Count(group.Members.Length)}: seed {Describe.Fate(decisions.Of(group.Seed))} {RecordNames.Describe(world.Targets[group.Seed.Index])}; "
        + $"{Describe.Space(world, group.Space)}, {Footprint(group)}.";

    /// <summary>"from (1200, -3400) to (2600, -2100)": the corners of the members' boxes seen from above.</summary>
    private static string Footprint(SeededGroup group)
    {
        var (min, max) = (group.FootprintMin, group.FootprintMax);
        return $"from ({TextFormat.Fixed(min.X, GroupPlaceDecimals)}, {TextFormat.Fixed(min.Y, GroupPlaceDecimals)}) "
            + $"to ({TextFormat.Fixed(max.X, GroupPlaceDecimals)}, {TextFormat.Fixed(max.Y, GroupPlaceDecimals)})";
    }
}
