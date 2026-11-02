using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

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

    /// <summary>
    /// What became of the other mods' objects the cleaned mod changed; the detailed log lists them by outcome. An object this step
    /// kept that went back where the cleaned mod was removed is counted with the kept ones and named as gone back. Nothing when there
    /// are none.
    /// </summary>
    /// <param name="restoredWhereRemoved">The judged record of each target that went back where the cleaned mod was removed.</param>
    public static LogSection BuildChangedObjectsSection(
        CollectedObjects world, IRemovalDecisions final, IReadOnlyDictionary<TargetId, PlacedChangeJudged> restoredWhereRemoved, PluginName target, ReportContext context)
    {
        var changed = FitOutcomes.ChangedObjects(world).ToImmutableArray();
        if (changed.IsEmpty) return new LogSection("changed-objects", []);
        var byOutcome = changed.ToLookup(changedObject => FitOutcomes.ChangedObjectOutcomeOf(final, restoredWhereRemoved, changedObject.Id));
        int CountOf(ChangedObjectOutcome outcome) => byOutcome[outcome].Count();
        var restoredLater = CountOf(ChangedObjectOutcome.KeptThenRestoredWhereRemoved);
        return new LogSection(
            "changed-objects",
            [
                $"{TargetOrigins.ChangedHeading(target)}: {TextFormat.Count(changed.Length)} ({TargetOrigins.CountedKinds(changed)}); "
                + $"kept where {target} put them: {TextFormat.Count(CountOf(ChangedObjectOutcome.KeptWherePut) + restoredLater)}"
                + (restoredLater > 0 ? $" ({TextFormat.Count(restoredLater)} then {RestoredWhereRemovedPhrase})" : string.Empty)
                + "; "
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

    private const string RestoredWhereRemovedPhrase = "restored where the cleaned mod was removed";

    private static string OutcomeText(ChangedObjectOutcome outcome, PluginName target, IRemovalDecisions final, TargetObject changedObject) => outcome switch
    {
        ChangedObjectOutcome.KeptWherePut => $"Kept where {target} put it",
        ChangedObjectOutcome.KeptThenRestoredWhereRemoved => $"Kept where {target} put it, then {RestoredWhereRemovedPhrase}",
        ChangedObjectOutcome.KeptShrunkOrTurned => "Kept there, shrunk or turned",
        ChangedObjectOutcome.Restored => RestoredText(((Decision.Restored)final.Of(changedObject.Id)!).EarlierVersion, target),
        ChangedObjectOutcome.Removed => "Removed",
        _ => throw new UnreachableException($"Unknown changed object outcome {outcome}."),
    };

    private static string RestoredText(EarlierVersion earlier, PluginName target) =>
        IsRestoredWhole(earlier) ? $"Restored as it was before {target}"
        : earlier.LeftToLaterPlugins.ContainsKey(PlacedParts.Position) ? $"Restored except its position, which stays as changed after {target}"
        : $"Restored where it stood before {target}";

    /// <summary>The base and data go back, and no part the cleaned mod changed stays.</summary>
    private static bool IsRestoredWhole(EarlierVersion earlier) =>
        earlier.Undo.HasFlag(PlacedParts.BaseAndData) && earlier.LeftToLaterPlugins.IsEmpty && earlier.KeptForRecordFormat == PlacedParts.None;

    private static string FittedObjectLine(string outcome, CollectedObjects world, IRemovalDecisions final, TargetObject fitted)
    {
        var change = FitOutcomes.ChangeOf(fitted, final) is { } shrunkOrTurned
            ? $"; size {TextFormat.Percent(shrunkOrTurned.SizeShare)}, turned {TextFormat.Fixed(shrunkOrTurned.TurnDegrees, 0)} degrees"
            : string.Empty;
        return $"  {outcome}: {RecordNames.Describe(fitted)} in {Describe.Location(world, fitted)}{change}.";
    }
}
