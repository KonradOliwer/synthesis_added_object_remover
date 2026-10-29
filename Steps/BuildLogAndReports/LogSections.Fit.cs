using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>
    /// The detailed log's count of the objects that may be shrunk or turned and are too close where they stand, with the step's time;
    /// the detailed log's step-time line alone when there is none.
    /// </summary>
    public static LogSection BuildFitSection(FitResult found, TimeSpan elapsed, ReportContext context)
    {
        if (!context.Detailed || found.Clashing == 0) return new LogSection("fit", StepTimeLines(FitStepName, elapsed, context));
        return new LogSection(
            "fit",
            [
                $"Objects too close where they stand: {TextFormat.Count(found.Clashing)}; "
                + $"not shrunk or turned because other objects rest on them: {TextFormat.Count(found.RestedOn)}; "
                + $"tried shrunk or turned: {TextFormat.Count(found.Tried)}; "
                + $"fit: {TextFormat.Count(found.Fits.Length)}{Describe.TimedSuffix(elapsed, context)}.",
            ]);
    }

    /// <summary>What became of the other mods' objects the cleaned mod changed; the detailed log lists them by outcome. Nothing when there are none.</summary>
    public static LogSection BuildChangedObjectsSection(CollectedObjects world, IRemovalDecisions final, PluginName target, ReportContext context)
    {
        var changed = FitOutcomes.ChangedObjects(world).ToImmutableArray();
        if (changed.IsEmpty) return new LogSection("changed-objects", []);
        var byOutcome = changed.ToLookup(changedObject => FitOutcomes.OutcomeOf(final, changedObject.Id));
        int CountOf(ChangedObjectOutcome outcome) => byOutcome[outcome].Count();
        return new LogSection(
            "changed-objects",
            [
                $"{TargetOrigins.ChangedHeading(target)}: {TextFormat.Count(changed.Length)} ({TargetOrigins.CountedKinds(changed)}); "
                + $"kept where {target} put them: {TextFormat.Count(CountOf(ChangedObjectOutcome.KeptWherePut))}; "
                + $"kept there shrunk or turned: {TextFormat.Count(CountOf(ChangedObjectOutcome.KeptShrunkOrTurned))}; "
                + (CountOf(ChangedObjectOutcome.Restored) is > 0 and var restored ? $"restored: {TextFormat.Count(restored)}; " : string.Empty)
                + $"removed: {TextFormat.Count(CountOf(ChangedObjectOutcome.Removed))}.",
                .. context.Detailed
                    ? Enum.GetValues<ChangedObjectOutcome>().SelectMany(outcome => byOutcome[outcome].Select(changedObject =>
                        FittedObjectLine(OutcomeText(outcome, target, final, changedObject), world, final, changedObject)))
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

    private static string OutcomeText(ChangedObjectOutcome outcome, PluginName target, IRemovalDecisions final, TargetObject changedObject) => outcome switch
    {
        ChangedObjectOutcome.KeptWherePut => $"Kept where {target} put it",
        ChangedObjectOutcome.KeptShrunkOrTurned => "Kept there, shrunk or turned",
        ChangedObjectOutcome.Restored => ((Decision.Restored)final.Of(changedObject.Id)!).EarlierVersion.RestoresWholeRecord
            ? $"Restored as it was before {target}"
            : $"Restored where it stood before {target}",
        ChangedObjectOutcome.Removed => "Removed",
        _ => throw new UnreachableException($"Unknown changed object outcome {outcome}."),
    };

    private static string FittedObjectLine(string outcome, CollectedObjects world, IRemovalDecisions final, TargetObject fitted)
    {
        var change = FitOutcomes.ChangeOf(fitted, final) is { } shrunkOrTurned
            ? $"; size {TextFormat.Percent(shrunkOrTurned.SizeShare)}, turned {TextFormat.Fixed(shrunkOrTurned.TurnDegrees, 0)} degrees"
            : string.Empty;
        return $"  {outcome}: {RecordNames.Describe(fitted)} in {Describe.Location(world, fitted)}{change}.";
    }
}
