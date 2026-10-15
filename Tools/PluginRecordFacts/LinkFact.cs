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
    Other,
}

/// <summary>One form link from a record into a target-plugin record, as read; it is not judged here.</summary>
/// <param name="SourceRecordType">The record type name of the linking record, e.g. "Quest".</param>
/// <param name="SourceIsWorldspace">Whether the linking record is a worldspace.</param>
public readonly record struct LinkFact(
    RecordKey Source,
    string? SourceEditorId,
    string SourceRecordType,
    RecordKey Target,
    RelationKind Relation,
    bool SourceIsPlaced,
    bool SourceIsWorldspace);
