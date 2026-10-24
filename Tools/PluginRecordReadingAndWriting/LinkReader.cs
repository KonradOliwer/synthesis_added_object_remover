using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Enumerates the form links from records into the records asked for as neutral <see cref="LinkFact"/>s;
/// which of them matter is decided by the caller.
/// </summary>
internal static class LinkReader
{
    /// <param name="isAskedFor">Whether a link into this record is read.</param>
    /// <param name="linkedRecords">When given, every record the placed record links to, other than its base object and itself, is added to it.</param>
    /// <remarks>
    /// Every link into a record asked for is read, the base object link and links to the record itself
    /// included. Which of them are target objects is decided later. The links are walked once for both.
    /// </remarks>
    public static void ReadFromPlaced(
        IPlacedGetter record,
        RecordKey recordKey,
        Func<RecordKey, bool> isAskedFor,
        PluginNames names,
        List<LinkFact> facts,
        ISet<RecordKey>? linkedRecords)
    {
        var baseKey = record.GetBaseKey(names)?.Record;
        foreach (var link in record.EnumerateFormLinks())
        {
            if (link.IsNull) continue;
            var linkedKey = names.KeyOf(link.FormKey);
            if (linkedKey != baseKey && linkedKey != recordKey) linkedRecords?.Add(linkedKey);
            if (!isAskedFor(linkedKey)) continue;
            var relation = linkedKey == baseKey ? RelationKind.BaseObject : DescribeRelation(record, link.FormKey);
            facts.Add(new LinkFact(
                recordKey, record.EditorID, record.Registration.Name, linkedKey, relation, SourceIsPlaced: true, SourceIsWorldspace: false, AliasFillFlags.None));
        }
    }

    /// <summary>
    /// Every version of every non-placed record of the given plugins is checked. Overridden versions,
    /// which the game does not use, are included as well; this is conservative. Placed records are
    /// left out because <see cref="ReadFromPlaced"/> covers their winning versions. Besides the links to
    /// the given records, each quest alias that fills with a target-plugin NPC as its Unique Actor is read.
    /// </summary>
    /// <returns>The records that could not be parsed.</returns>
    /// <remarks>
    /// Mutagen reads a record's subrecords lazily, so a malformed one throws while its links are enumerated, not while the mod's
    /// records are enumerated. Only that exception is caught, per record: the links read before it stay, and the rest of the mod is read.
    /// </remarks>
    public static ImmutableArray<UnreadableRecord> ReadFromNonPlaced(
        IEnumerable<ISkyrimModGetter> mods,
        IReadOnlySet<RecordKey> records,
        PluginName target,
        PluginNames names,
        List<LinkFact> facts)
    {
        var unreadable = ImmutableArray.CreateBuilder<UnreadableRecord>();
        foreach (var mod in mods)
        {
            foreach (var record in mod.EnumerateMajorRecords())
            {
                if (record is IPlacedGetter) continue;
                try
                {
                    ReadLinksToRecords(record, records, names, facts);
                    if (record is IQuestGetter quest) ReadUniqueActors(quest, target, names, facts);
                }
                catch (MalformedDataException ex)
                {
                    unreadable.Add(new UnreadableRecord(
                        names.KeyOf(record.FormKey), record.EditorID, record.Registration.Name, ex.Message));
                }
            }
        }
        return unreadable.ToImmutable();
    }

    private static void ReadLinksToRecords(IMajorRecordGetter record, IReadOnlySet<RecordKey> records, PluginNames names, List<LinkFact> facts)
    {
        foreach (var link in EnumerateDependencyLinks(record))
        {
            if (link.IsNull) continue;
            var linkedKey = names.KeyOf(link.FormKey);
            if (!records.Contains(linkedKey)) continue;
            facts.Add(CreateNonPlacedFact(record, linkedKey, DescribeNonPlacedRelation(record, link.FormKey), AliasFillFlags.None, names));
        }
    }

    private static IEnumerable<IFormLinkGetter> EnumerateDependencyLinks(IMajorRecordGetter record) =>
        record is ILocationGetter location ? EnumerateLocationDependencyLinks(location) : record.EnumerateFormLinks(iterateNestedRecords: false);

    /// <remarks>
    /// Of a location, only what someone set as a role counts: the references listed with a location ref type (ACSR, LCSR),
    /// by which quest aliases pick them, and the world location marker (MNAM) and horse marker (NAM0). The Creation Kit
    /// builds the other reference lists from the references' own data: persistent refs (ACPR, LCPR) from their persist
    /// location, unique actors (ACUN, LCUN) from the placed unique NPCs, enable-parent refs (ACEP, LCEP) from their Enable
    /// Parent, initially disabled refs (ACID, LCID) from their flag. They say nothing the reference does not say itself,
    /// so nothing depends on the reference through them. The removed lists (RCPR, RCUN, RCSR) name what an override
    /// takes out of the master's lists. The location's other fields name no placed record.
    /// </remarks>
    private static IEnumerable<IFormLinkGetter> EnumerateLocationDependencyLinks(ILocationGetter location) =>
        ListsWithRefType(location)
            .Select(entry => (IFormLinkGetter)entry.Ref)
            .Append(location.WorldLocationMarkerRef)
            .Append(location.HorseMarkerRef);

    private static void ReadUniqueActors(IQuestGetter quest, PluginName target, PluginNames names, List<LinkFact> facts)
    {
        foreach (var alias in quest.Aliases)
        {
            if (alias.UniqueActor.IsNull || names.Of(alias.UniqueActor.FormKey.ModKey) != target) continue;
            facts.Add(CreateNonPlacedFact(quest, names.KeyOf(alias.UniqueActor.FormKey), RelationKind.UniqueActor, ReadFillFlags(alias), names));
        }
    }

    private static AliasFillFlags ReadFillFlags(IQuestAliasGetter alias)
    {
        var flags = alias.Flags ?? 0;
        return (flags.HasFlag(QuestAlias.Flag.Optional) ? AliasFillFlags.Optional : AliasFillFlags.None)
            | (flags.HasFlag(QuestAlias.Flag.AllowDisabled) ? AliasFillFlags.AllowDisabled : AliasFillFlags.None);
    }

    private static LinkFact CreateNonPlacedFact(
        IMajorRecordGetter record, RecordKey linkedKey, RelationKind relation, AliasFillFlags aliasFlags, PluginNames names) =>
        new(
            names.KeyOf(record.FormKey),
            record.EditorID,
            record.Registration.Name,
            linkedKey,
            relation,
            SourceIsPlaced: false,
            SourceIsWorldspace: record is IWorldspaceGetter,
            aliasFlags);

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
