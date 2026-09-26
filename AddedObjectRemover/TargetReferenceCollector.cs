using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Records the links to target-plugin objects. A link from a checked target object to another
/// target-plugin FormKey joins a linked group; any other link keeps the linked object: from other
/// placed records (labelled as Enable Parent, Linked Reference, Activate Parent, Attach Ref or
/// teleport destination where the link is one of those), from non-placed records such as quest
/// aliases, AI packages, locations, factions, navmesh doors, conditions and script properties,
/// and every teleport destination.
/// </summary>
internal static class TargetReferenceCollector
{
    private const string PlacedCategoryPrefix = "placed object: ";
    private const string TeleportDestination = "teleport destination";

    /// <param name="isTargetObject">The record is a checked target object, whose links to other target objects group them.</param>
    /// <remarks>Only links to target-plugin FormKeys are recorded; which of them are target objects is decided later.</remarks>
    public static void CollectFromPlaced(
        IPlacedGetter record,
        bool isTargetObject,
        ModKey target,
        Dictionary<FormKey, KeepReason> references,
        List<TargetLink> links)
    {
        foreach (var link in record.EnumerateFormLinks())
        {
            var formKey = link.FormKey;
            if (link.IsNull || formKey.ModKey != target || formKey == record.FormKey) continue;
            if (isTargetObject && !IsTeleportDestination(record, formKey))
            {
                links.Add(new TargetLink(record.FormKey, formKey));
            }
            else if (!references.ContainsKey(formKey))
            {
                references[formKey] = CreatePlacedReason(record, formKey);
            }
        }
    }

    private static KeepReason CreatePlacedReason(IPlacedGetter record, FormKey linked)
    {
        var relation = DescribeRelation(record, linked);
        var kind = relation == TeleportDestination ? KeepKind.TeleportDoor : KeepKind.PlacedReference;
        return new KeepReason(kind, PlacedCategoryPrefix + relation, $"{relation} of {record.FormKey}");
    }

    /// <summary>
    /// Every version of every non-placed record of the given plugins is checked. Overridden versions,
    /// which the game does not use, are included as well; this is conservative.
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
                    references[link.FormKey] = new KeepReason(KeepKind.NonPlacedReference, $"{type} record", $"linked from {type} {RecordNames.Describe(record)}");
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
        if (IsTeleportDestination(record, linked)) return TeleportDestination;
        if (record.EnableParent?.Reference.FormKey == linked) return "Enable Parent";
        if (GetLinkedReferences(record).Any(reference => reference.Reference.FormKey == linked)) return "Linked Reference";
        if (GetActivateParents(record).Any(parent => parent.Reference.FormKey == linked)) return "Activate Parent";
        if (record is IPlacedObjectGetter placedObject && placedObject.AttachRef.FormKey == linked) return "Attach Ref";
        return "other link";
    }

    private static bool IsTeleportDestination(IPlacedGetter record, FormKey linked) =>
        record is IPlacedObjectGetter { TeleportDestination: { } destination } && destination.Door.FormKey == linked;

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
