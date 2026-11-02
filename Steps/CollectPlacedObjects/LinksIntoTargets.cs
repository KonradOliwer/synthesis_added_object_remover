using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

internal static class LinksIntoTargets
{
    public static HashSet<RecordKey> KeysOf(IEnumerable<TargetObject> targets) => targets.Select(target => target.Key).ToHashSet();

    /// <summary>
    /// The links into the target plugin and into the changed objects, without those from removed records. The reader also gives
    /// the links into the records the target overrides and leaves where they were, which judge nothing.
    /// </summary>
    public static ImmutableArray<LinkFact> Keep(
        IEnumerable<LinkFact> links, IReadOnlySet<RecordKey> removed, PluginName cleanedMod, IReadOnlySet<RecordKey> targetKeys) =>
        [.. links.Where(link => !removed.Contains(link.Source) && (link.Target.Plugin == cleanedMod || targetKeys.Contains(link.Target)))];
}
