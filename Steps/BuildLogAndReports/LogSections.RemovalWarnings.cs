using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static partial class LogSections
{
    /// <summary>
    /// One line per removed unique NPC, in target order, then one line with the number of removed large references;
    /// empty when no such object was removed.
    /// </summary>
    public static LogSection BuildRemovalWarningsSection(CollectedObjects world, IEnumerable<RemovedObject> removals, RemovalRisks risks)
    {
        var removed = removals.Select(removal => removal.TargetIndex).ToHashSet();
        var uniqueActorLines = RemovedUniqueActors(risks, removed).Select(role => UniqueActorLine(world, role));
        var largeReferences = RemovedLargeReferences(risks, removed).Count();
        string[] largeReferenceLines = largeReferences > 0
            ? [$"Warning: {TextFormat.Count(largeReferences)} removed objects are in their worldspace's large-reference list; "
               + "distant LOD made before this run still shows them."]
            : [];
        return new("removalWarnings", [.. uniqueActorLines, .. largeReferenceLines]);
    }

    private static IEnumerable<UniqueActorRole> RemovedUniqueActors(RemovalRisks risks, IReadOnlySet<int> removed) =>
        risks.UniqueActors.Where(role => removed.Contains(role.Target.Index));

    /// <returns>The target indices.</returns>
    private static IEnumerable<int> RemovedLargeReferences(RemovalRisks risks, IReadOnlySet<int> removed) =>
        risks.LargeReferences.Select(id => id.Index).Where(removed.Contains);

    private static string UniqueActorLine(CollectedObjects world, UniqueActorRole role)
    {
        var target = world.Targets[role.Target.Index];
        var quest = role.Alias;
        return $"Warning: removed unique NPC {RecordNames.Describe(target)} in {Describe.Location(world, target)}: "
            + $"{quest.SourceRecordType} {RecordLabels.Of(quest.Source, quest.SourceEditorId)} fills an alias with it as a Unique Actor; "
            + "a disabled actor cannot fill that alias.";
    }
}
