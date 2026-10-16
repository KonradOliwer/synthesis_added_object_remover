using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Enumerates the form links from records into target-plugin records as neutral <see cref="LinkFact"/>s;
/// which of them matter is decided by the caller.
/// </summary>
internal static class LinkReader
{
    /// <remarks>
    /// Every link into the target plugin is read, the base object link and links to the record itself
    /// included. Which of them are target objects is decided later.
    /// </remarks>
    public static void ReadFromPlaced(
        IPlacedGetter record,
        RecordKey recordKey,
        PluginName target,
        PluginNames names,
        List<LinkFact> facts)
    {
        var baseKey = record.GetBaseKey(names)?.Record;
        foreach (var link in record.EnumerateFormLinks())
        {
            if (link.IsNull || names.Of(link.FormKey.ModKey) != target) continue;
            var linkedKey = names.KeyOf(link.FormKey);
            var relation = linkedKey == baseKey ? RelationKind.BaseObject : DescribeRelation(record, link.FormKey);
            facts.Add(new LinkFact(recordKey, record.EditorID, record.Registration.Name, linkedKey, relation, SourceIsPlaced: true, SourceIsWorldspace: false));
        }
    }

    /// <summary>
    /// Every version of every non-placed record of the given plugins is checked. Overridden versions,
    /// which the game does not use, are included as well; this is conservative. Placed records are
    /// left out because <see cref="ReadFromPlaced"/> covers their winning versions. Besides the links to
    /// the given records, each quest alias that fills with a target-plugin NPC as its Unique Actor is read.
    /// </summary>
    public static void ReadFromNonPlaced(
        IEnumerable<ISkyrimModGetter> mods,
        IReadOnlySet<RecordKey> targets,
        PluginName target,
        PluginNames names,
        List<LinkFact> facts)
    {
        foreach (var mod in mods)
        {
            foreach (var record in mod.EnumerateMajorRecords())
            {
                if (record is IPlacedGetter) continue;
                ReadLinksToTargets(record, targets, target, names, facts);
                if (record is IQuestGetter quest) ReadUniqueActors(quest, target, names, facts);
            }
        }
    }

    private static void ReadLinksToTargets(IMajorRecordGetter record, IReadOnlySet<RecordKey> targets, PluginName target, PluginNames names, List<LinkFact> facts)
    {
        foreach (var link in record.EnumerateFormLinks(iterateNestedRecords: false))
        {
            if (link.IsNull || names.Of(link.FormKey.ModKey) != target) continue;
            var linkedKey = names.KeyOf(link.FormKey);
            if (!targets.Contains(linkedKey)) continue;
            facts.Add(CreateNonPlacedFact(record, linkedKey, DescribeNonPlacedRelation(record, link.FormKey), names));
        }
    }

    private static void ReadUniqueActors(IQuestGetter quest, PluginName target, PluginNames names, List<LinkFact> facts)
    {
        foreach (var alias in quest.Aliases)
        {
            if (alias.UniqueActor.IsNull || names.Of(alias.UniqueActor.FormKey.ModKey) != target) continue;
            facts.Add(CreateNonPlacedFact(quest, names.KeyOf(alias.UniqueActor.FormKey), RelationKind.UniqueActor, names));
        }
    }

    private static LinkFact CreateNonPlacedFact(IMajorRecordGetter record, RecordKey linkedKey, RelationKind relation, PluginNames names) =>
        new(
            names.KeyOf(record.FormKey),
            record.EditorID,
            record.Registration.Name,
            linkedKey,
            relation,
            SourceIsPlaced: false,
            SourceIsWorldspace: record is IWorldspaceGetter);

    private static RelationKind DescribeNonPlacedRelation(IMajorRecordGetter record, FormKey linked) =>
        record is ILocationGetter location && ListsWithRefType(location).Any(entry => entry.Ref.FormKey == linked)
            ? RelationKind.LocationRefType
            : RelationKind.Other;

    private static IEnumerable<ILocationRefTypeReferenceGetter> ListsWithRefType(ILocationGetter location) =>
        (location.LocationRefTypeReferencesAdded ?? []).Concat(location.LocationRefTypeReferencesStatic ?? []);

    private static RelationKind DescribeRelation(IPlacedGetter record, FormKey linked)
    {
        if (IsTeleportDestination(record, linked)) return RelationKind.TeleportDestination;
        if (record.EnableParent?.Reference.FormKey == linked) return RelationKind.EnableParent;
        if (GetLinkedReferences(record).Any(reference => reference.Reference.FormKey == linked)) return RelationKind.LinkedReference;
        if (GetActivateParents(record).Any(parent => parent.Reference.FormKey == linked)) return RelationKind.ActivateParent;
        if (record is IPlacedObjectGetter placedObject && placedObject.AttachRef.FormKey == linked) return RelationKind.AttachRef;
        return RelationKind.Other;
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
