using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Records target-plugin objects that other records link to: placed records through any of their
/// form links (labelled as Enable Parent, Linked Reference, Activate Parent or teleport destination
/// where the link is one of those), and non-placed records such as quest aliases, AI packages,
/// locations, factions, navmesh doors, conditions and script properties.
/// </summary>
internal static class TargetReferenceCollector
{
    private const string PlacedCategoryPrefix = "placed object: ";

    /// <remarks>The target is not known yet during the placed scan, so links to non-placed target records are recorded too but never looked up.</remarks>
    public static void CollectFromPlaced(IPlacedGetter record, ModKey target, Dictionary<FormKey, KeepReason> references)
    {
        foreach (var link in record.EnumerateFormLinks())
        {
            var formKey = link.FormKey;
            if (link.IsNull || formKey.ModKey != target || formKey == record.FormKey || references.ContainsKey(formKey)) continue;
            var relation = DescribeRelation(record, formKey);
            references[formKey] = new KeepReason(PlacedCategoryPrefix + relation, $"{relation} of {record.FormKey}");
        }
    }

    /// <summary>
    /// Every version of every non-placed record of the given plugins is checked, overridden ones
    /// included, so no link the game might still follow is missed.
    /// </summary>
    public static void CollectFromNonPlaced(IEnumerable<ISkyrimModGetter> mods, IReadOnlySet<FormKey> targets, Dictionary<FormKey, KeepReason> references)
    {
        foreach (var mod in mods)
        {
            foreach (var record in mod.EnumerateMajorRecords())
            {
                if (IsSkippedAsNonPlacedSource(record)) continue;
                foreach (var link in record.EnumerateFormLinks(iterateNestedRecords: false))
                {
                    if (!targets.Contains(link.FormKey) || references.ContainsKey(link.FormKey)) continue;
                    var type = record.Registration.Name;
                    references[link.FormKey] = new KeepReason($"{type} record", $"linked from {type} {RecordNames.Describe(record)}");
                }
            }
        }
    }

    /// <summary>
    /// Placed records are covered by <see cref="CollectFromPlaced"/>. A worldspace links placed
    /// objects only through its large-reference list, which only speeds up loading.
    /// </summary>
    private static bool IsSkippedAsNonPlacedSource(IMajorRecordGetter record) => record is IPlacedGetter or IWorldspaceGetter;

    private static string DescribeRelation(IPlacedGetter record, FormKey linked)
    {
        if (record.EnableParent?.Reference.FormKey == linked) return "Enable Parent";
        if (GetLinkedReferences(record).Any(reference => reference.Reference.FormKey == linked)) return "Linked Reference";
        if (record is IPlacedObjectGetter { TeleportDestination: { } destination } && destination.Door.FormKey == linked)
        {
            return "teleport destination";
        }
        if (GetActivateParents(record).Any(parent => parent.Reference.FormKey == linked)) return "Activate Parent";
        return "other link";
    }

    private static IEnumerable<ILinkedReferencesGetter> GetLinkedReferences(IPlacedGetter record) => record switch
    {
        IPlacedObjectGetter placedObject => placedObject.LinkedReferences,
        IPlacedNpcGetter placedNpc => placedNpc.LinkedReferences,
        IAPlacedTrapGetter placedTrap => placedTrap.LinkedReferences,
        _ => [],
    };

    private static IEnumerable<IActivateParentGetter> GetActivateParents(IPlacedGetter record) =>
        (record switch
        {
            IPlacedObjectGetter placedObject => placedObject.ActivateParents,
            IPlacedNpcGetter placedNpc => placedNpc.ActivateParents,
            IAPlacedTrapGetter placedTrap => placedTrap.ActivateParents,
            _ => null,
        })?.Parents ?? [];
}
