using System.Diagnostics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>The detailed log's count of the objects tried at other placements; nothing when none was tried.</summary>
    public static LogSection BuildFitSection(FitResult found, TimeSpan elapsed, ReportContext context)
    {
        if (!context.Detailed || found.Tried == 0) return new LogSection("fit", []);
        var movedBack = found.Fits.Count(fit => fit.Kind != FitKind.ShrunkOrTurned);
        return new LogSection(
            "fit",
            [
                $"Tried {TextFormat.Count(found.Tried)} objects that clash where they stand at other placements: {TextFormat.Count(found.Fits.Length)} fit "
                + $"({TextFormat.Count(movedBack)} of them moved back){Describe.TimedSuffix(elapsed, context)}.",
            ]);
    }

    /// <summary>What became of the moved replacements; the detailed log lists them by outcome. Nothing when there are none.</summary>
    public static LogSection BuildMovedReplacementsSection(CollectedObjects world, IRemovalDecisions final, PluginName target, ReportContext context)
    {
        var byOutcome = FitOutcomes.MovedReplacements(world).ToLookup(replacement => FitOutcomes.OutcomeOf(final, replacement.Id));
        var total = byOutcome.Sum(group => group.Count());
        if (total == 0) return new LogSection("moved-replacements", []);
        int CountOf(ReplacementOutcome outcome) => byOutcome[outcome].Count();
        return new LogSection(
            "moved-replacements",
            [
                $"Objects of other mods that {target} moved: {TextFormat.Count(total)}; "
                + $"kept where {target} put them: {TextFormat.Count(CountOf(ReplacementOutcome.KeptWhereMoved))}; "
                + $"kept there shrunk or turned: {TextFormat.Count(CountOf(ReplacementOutcome.KeptShrunkOrTurned))}; "
                + $"moved back: {TextFormat.Count(CountOf(ReplacementOutcome.MovedBack))}; "
                + $"moved back and shrunk or turned: {TextFormat.Count(CountOf(ReplacementOutcome.MovedBackShrunkOrTurned))}; "
                + $"removed: {TextFormat.Count(CountOf(ReplacementOutcome.Removed))}.",
                .. context.Detailed
                    ? Enum.GetValues<ReplacementOutcome>().SelectMany(outcome => byOutcome[outcome].Select(replacement =>
                        FittedObjectLine(OutcomeText(outcome, target), world, final, replacement)))
                    : [],
            ]);
    }

    /// <summary>The target plugin's own objects kept shrunk or turned; the detailed log lists them. Nothing when there are none.</summary>
    public static LogSection BuildOwnObjectsShrunkOrTurnedSection(CollectedObjects world, IRemovalDecisions final, PluginName target, ReportContext context)
    {
        var fitted = FitOutcomes.OwnObjectsShrunkOrTurned(world, final).ToList();
        if (fitted.Count == 0) return new LogSection("own-objects-shrunk-or-turned", []);
        return new LogSection(
            "own-objects-shrunk-or-turned",
            [
                $"{target} objects kept shrunk or turned: {TextFormat.Count(fitted.Count)}.",
                .. context.Detailed ? fitted.Select(own => FittedObjectLine("Kept shrunk or turned", world, final, own)) : [],
            ]);
    }

    private static string OutcomeText(ReplacementOutcome outcome, PluginName target) => outcome switch
    {
        ReplacementOutcome.KeptWhereMoved => $"Kept where {target} put it",
        ReplacementOutcome.KeptShrunkOrTurned => "Kept there, shrunk or turned",
        ReplacementOutcome.MovedBack => "Moved back",
        ReplacementOutcome.MovedBackShrunkOrTurned => "Moved back, shrunk or turned",
        ReplacementOutcome.Removed => "Removed",
        _ => throw new UnreachableException($"Unknown replacement outcome {outcome}."),
    };

    private static string FittedObjectLine(string outcome, CollectedObjects world, IRemovalDecisions final, TargetObject fitted)
    {
        var change = FitOutcomes.ChangeOf(fitted, final) is { } shrunkOrTurned
            ? $"; size {TextFormat.Percent(shrunkOrTurned.SizeShare)}, turned {TextFormat.Fixed(shrunkOrTurned.TurnDegrees, 0)} degrees"
            : string.Empty;
        return $"  {outcome}: {RecordNames.Describe(fitted)} in {Describe.Location(world, fitted)}{change}.";
    }
}
