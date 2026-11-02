using System.Collections.Immutable;
using Mutagen.Bethesda.Plugins;
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
    /// included. Which of them matter is decided by the caller. The links are walked once for both.
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
    /// the given records, each quest alias that fills with an NPC of the given plugin as its Unique Actor is read.
    /// </summary>
    /// <returns>The links and the records that could not be parsed, both in the order of the given plugins and of their records.</returns>
    /// <param name="mods">In priority order, highest first.</param>
    /// <remarks>
    /// The plugins are started from the last one, since the large masters have the lowest priority and the run waits
    /// for the slowest plugin. The results are joined by index, so their order does not depend on the thread count.
    /// </remarks>
    public static NonPlacedLinks ReadFromNonPlaced(
        IReadOnlyList<ISkyrimModGetter> mods,
        IReadOnlySet<RecordKey> records,
        PluginName plugin,
        PluginNames names,
        Execution execution)
    {
        var lowestPriorityFirst = new WorkOrder([.. Enumerable.Range(0, mods.Count).Reverse()]);
        var perMod = ParallelMap.Run(
            execution, lowestPriorityFirst, mods.Count, index => ReadPluginLinks(mods[index], records, plugin, names), ParallelMap.OneItemPerRange);
        return new NonPlacedLinks(
            [.. perMod.SelectMany(read => read.Links)], [.. perMod.SelectMany(read => read.Unreadable)]);
    }

    /// <remarks>
    /// Mutagen reads a record's subrecords lazily, so a malformed one throws while its links are read, not while the mod's
    /// records are enumerated. Only malformed data is caught, per record: the links read before it stay, and the rest of the mod is read.
    /// </remarks>
    private static NonPlacedLinks ReadPluginLinks(
        ISkyrimModGetter mod, IReadOnlySet<RecordKey> records, PluginName plugin, PluginNames names)
    {
        var facts = new List<LinkFact>();
        var unreadable = ImmutableArray.CreateBuilder<UnreadableRecord>();
        foreach (var record in mod.EnumerateMajorRecords())
        {
            if (record is IPlacedGetter) continue;
            try
            {
                ReadLinksToRecords(record, records, names, facts);
                if (record is IQuestGetter quest) ReadUniqueActors(quest, plugin, names, facts);
            }
            catch (MalformedRecordException ex)
            {
                unreadable.Add(new UnreadableRecord(
                    names.KeyOf(record.FormKey), ReadEditorIdOrNull(record), record.Registration.Name, ex.Message));
            }
        }
        return new NonPlacedLinks([.. facts], unreadable.ToImmutable());
    }

    /// <remarks>The record is already known to be malformed, and the malformed part may be its editor ID: it is then left without one.</remarks>
    private static string? ReadEditorIdOrNull(IMajorRecordGetter record)
    {
        try
        {
            return MutagenCalls.Read(() => record.EditorID);
        }
        catch (MalformedRecordException)
        {
            return null;
        }
    }

    private static void ReadLinksToRecords(IMajorRecordGetter record, IReadOnlySet<RecordKey> records, PluginNames names, List<LinkFact> facts)
    {
        var (links, withRefType) = record is ILocationGetter location ? ReadLocationDependencyLinks(location) : (ReadFormLinks(record), []);
        foreach (var link in links)
        {
            if (link.IsNull) continue;
            var linkedKey = names.KeyOf(link);
            if (!records.Contains(linkedKey)) continue;
            facts.Add(CreateNonPlacedFact(record, linkedKey, withRefType.Contains(link) ? RelationKind.LocationRefType : RelationKind.Other, AliasFillFlags.None, names));
        }
    }

    private static FormKey[] ReadFormLinks(IMajorRecordGetter record) =>
        MutagenCalls.Read(() => record.EnumerateFormLinks(iterateNestedRecords: false).Select(link => link.FormKey).ToArray());

    /// <returns>The location's dependency links, and of them the references listed with a location ref type.</returns>
    /// <remarks>
    /// Of a location, only what someone set as a role counts: the references listed with a location ref type (ACSR, LCSR),
    /// by which quest aliases pick them, and the world location marker (MNAM) and horse marker (NAM0). The Creation Kit
    /// builds the other reference lists from the references' own data: persistent refs (ACPR, LCPR) from their persist
    /// location, unique actors (ACUN, LCUN) from the placed unique NPCs, enable-parent refs (ACEP, LCEP) from their Enable
    /// Parent, initially disabled refs (ACID, LCID) from their flag. They say nothing the reference does not say itself,
    /// so nothing depends on the reference through them. The removed lists (RCPR, RCUN, RCSR) name what an override
    /// takes out of the master's lists. The location's other fields name no placed record.
    /// </remarks>
    private static (FormKey[] Links, FormKey[] WithRefType) ReadLocationDependencyLinks(ILocationGetter location)
    {
        var (withRefType, worldMarker, horseMarker) = MutagenCalls.Read(() => (
            (location.LocationRefTypeReferencesAdded ?? []).Concat(location.LocationRefTypeReferencesStatic ?? []).Select(entry => entry.Ref.FormKey).ToArray(),
            location.WorldLocationMarkerRef.FormKey,
            location.HorseMarkerRef.FormKey));
        return ([.. withRefType, worldMarker, horseMarker], withRefType);
    }

    private static void ReadUniqueActors(IQuestGetter quest, PluginName plugin, PluginNames names, List<LinkFact> facts)
    {
        var aliases = MutagenCalls.Read(() => quest.Aliases.Select(alias => (Actor: alias.UniqueActor.FormKey, alias.Flags)).ToArray());
        foreach (var (actor, flags) in aliases)
        {
            if (actor.IsNull || names.Of(actor.ModKey) != plugin) continue;
            facts.Add(CreateNonPlacedFact(quest, names.KeyOf(actor), RelationKind.UniqueActor, ToFillFlags(flags ?? 0), names));
        }
    }

    private static AliasFillFlags ToFillFlags(QuestAlias.Flag flags) =>
        (flags.HasFlag(QuestAlias.Flag.Optional) ? AliasFillFlags.Optional : AliasFillFlags.None)
        | (flags.HasFlag(QuestAlias.Flag.AllowDisabled) ? AliasFillFlags.AllowDisabled : AliasFillFlags.None);

    private static LinkFact CreateNonPlacedFact(
        IMajorRecordGetter record, RecordKey linkedKey, RelationKind relation, AliasFillFlags aliasFlags, PluginNames names) =>
        new(
            names.KeyOf(record.FormKey),
            MutagenCalls.Read(() => record.EditorID),
            record.Registration.Name,
            linkedKey,
            relation,
            SourceIsPlaced: false,
            SourceIsWorldspace: record is IWorldspaceGetter,
            aliasFlags);

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
