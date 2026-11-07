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
    /// kept, where the cleaned mod put it or shrunk or turned, that the step that restores where the cleaned mod was removed then put
    /// back, or that its follow-up of what went back restored or removed, is counted with the kept ones and named with what happened
    /// next. Nothing when there are none.
    /// </summary>
    /// <param name="whereRemoved">What the step that restores where the cleaned mod was removed did to each target it acted on.</param>
    public static LogSection BuildChangedObjectsSection(
        CollectedObjects world, IRemovalDecisions final, IReadOnlyDictionary<TargetId, WhereRemovedOutcome> whereRemoved, PluginName target, ReportContext context)
    {
        var changed = FitOutcomes.ChangedObjects(world).ToImmutableArray();
        if (changed.IsEmpty) return new LogSection("changed-objects", []);
        var fates = changed.ToDictionary(changedObject => changedObject.Id, changedObject => FitOutcomes.FateOf(final, whereRemoved, changedObject.Id));
        string Counted(ChangedObjectOutcome run) => CountWithWhatHappenedNext([.. fates.Values.Where(fate => fate.Run == run)]);
        var putThereBy = world.ChangedRecords.Where(record => record is { Target: not null, PutThereBy: not null }).ToDictionary(record => record.Target!.Value, record => record.PutThereBy!.Value);
        return new LogSection(
            "changed-objects",
            [
                $"{TargetOrigins.ChangedHeading(target)}: {TextFormat.Count(changed.Length)} ({TargetOrigins.CountedKinds(changed)}); "
                + $"kept where {target} put them: {Counted(ChangedObjectOutcome.KeptWherePut)}; "
                + $"kept there shrunk or turned: {Counted(ChangedObjectOutcome.KeptShrunkOrTurned)}; "
                + (fates.Values.Any(fate => fate.Run == ChangedObjectOutcome.Restored) ? $"restored: {Counted(ChangedObjectOutcome.Restored)}; " : string.Empty)
                + $"removed: {Counted(ChangedObjectOutcome.Removed)}.",
                .. context.Detailed
                    ? changed.OrderBy(changedObject => FateOrder(fates[changedObject.Id])).Select(changedObject =>
                        FittedObjectLine(OutcomeText(fates[changedObject.Id], PutThereBy(changedObject, putThereBy, target), target, final, changedObject), world, final, changedObject))
                    : [],
            ]);
    }

    /// <summary>"5 (1 then restored where the cleaned mod was removed, 1 then removed by ...)": the brackets only when the restore did something to one.</summary>
    private static string CountWithWhatHappenedNext(List<ChangedObjectFate> fates)
    {
        List<string> next =
        [
            .. Enum.GetValues<WhereRemovedThen>()
                .Select(then => (Then: then, Count: fates.Count(fate => fate.Then == then)))
                .Where(entry => entry.Count > 0)
                .Select(entry => $"{TextFormat.Count(entry.Count)} then {FitOutcomes.ThenPhrase(entry.Then)}"),
        ];
        return TextFormat.Count(fates.Count) + (next.Count > 0 ? $" ({string.Join(", ", next)})" : string.Empty);
    }

    /// <summary>By what the removal steps decided, then those the restore did nothing to before those it did something to, in the order of what it did.</summary>
    private static (ChangedObjectOutcome Run, int Then) FateOrder(ChangedObjectFate fate) => (fate.Run, fate.Then is { } then ? (int)then + 1 : 0);

    /// <summary>The target plugin's own objects kept shrunk or turned; the detailed log lists them. Nothing when there are none.</summary>
    /// <param name="whereRemoved">What the step that restores where the cleaned mod was removed did to each target it acted on.</param>
    public static LogSection BuildOwnObjectsShrunkOrTurnedSection(
        CollectedObjects world, IRemovalDecisions final, IReadOnlyDictionary<TargetId, WhereRemovedOutcome> whereRemoved, PluginName target, ReportContext context)
    {
        var fitted = FitOutcomes.OwnObjectsShrunkOrTurned(world, final, whereRemoved).ToList();
        if (fitted.Count == 0) return new LogSection("own-objects-shrunk-or-turned", []);
        return new LogSection(
            "own-objects-shrunk-or-turned",
            [
                $"{target} objects kept shrunk or turned: {TextFormat.Count(fitted.Count)}.",
                .. context.Detailed ? fitted.Select(own => FittedObjectLine("Kept shrunk or turned", world, final, own)) : [],
            ]);
    }

    /// <summary>The plugin that put the object where it stands: the winning plugin when it is neither the cleaned mod nor the patch.</summary>
    private static PluginName PutThereBy(TargetObject changedObject, IReadOnlyDictionary<TargetId, PluginName> putThereBy, PluginName cleanedMod) =>
        putThereBy.TryGetValue(changedObject.Id, out var winner) ? winner : cleanedMod;

    private static string OutcomeText(ChangedObjectFate fate, PluginName putBy, PluginName target, IRemovalDecisions final, TargetObject changedObject) =>
        RunOutcomeText(fate.Run, putBy, target, final, changedObject) + FitOutcomes.ThenSuffix(fate);

    private static string RunOutcomeText(ChangedObjectOutcome outcome, PluginName putBy, PluginName target, IRemovalDecisions final, TargetObject changedObject) => outcome switch
    {
        ChangedObjectOutcome.KeptWherePut => $"Kept where {putBy} put it",
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
