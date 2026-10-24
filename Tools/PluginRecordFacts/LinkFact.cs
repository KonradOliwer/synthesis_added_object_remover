using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>How a record links to another one, as the linking record itself says.</summary>
public enum RelationKind
{
    EnableParent,
    LinkedReference,
    ActivateParent,
    AttachRef,
    TeleportDestination,
    BaseObject,

    /// <summary>A location lists the record together with a location ref type (ACSR, LCSR).</summary>
    LocationRefType,

    /// <summary>A quest alias fills with the NPC as its Unique Actor (ALUA); the linked record is the NPC base, not a placed one.</summary>
    UniqueActor,
    Other,
}

/// <summary>The quest alias flags (FNAM) that say whether the alias can stay empty or fill with a disabled reference.</summary>
[Flags]
public enum AliasFillFlags
{
    None = 0,
    Optional = 1,
    AllowDisabled = 2,
}

/// <summary>A record whose data the game library could not parse; the links read from it before the problem are real, the rest are unknown.</summary>
/// <param name="RecordType">The record type name, e.g. "Perk".</param>
/// <param name="Problem">What the game library reported.</param>
public sealed record UnreadableRecord(RecordKey Key, string? EditorId, string RecordType, string Problem);

/// <summary>What reading the links of the non-placed records produced.</summary>
/// <param name="Links">The links read.</param>
/// <param name="Unreadable">The records that could not be read, in the order found.</param>
public sealed record NonPlacedLinks(ImmutableArray<LinkFact> Links, ImmutableArray<UnreadableRecord> Unreadable);

/// <summary>One form link from a record into a target-plugin record, as read; it is not judged here.</summary>
/// <param name="SourceRecordType">The record type name of the linking record, e.g. "Quest".</param>
/// <param name="SourceIsWorldspace">Whether the linking record is a worldspace.</param>
/// <param name="AliasFlags">For a <see cref="RelationKind.UniqueActor"/> link, the flags of that alias; otherwise none.</param>
public readonly record struct LinkFact(
    RecordKey Source,
    string? SourceEditorId,
    string SourceRecordType,
    RecordKey Target,
    RelationKind Relation,
    bool SourceIsPlaced,
    bool SourceIsWorldspace,
    AliasFillFlags AliasFlags);
