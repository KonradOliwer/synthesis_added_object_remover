using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>What every record kind's parts leave out: the record's identity and the stamps of its header.</summary>
internal static class RecordHeader
{
    public static readonly string[] Fields =
    [
        nameof(IMajorRecordGetter.FormKey),
        nameof(IMajorRecordGetter.EditorID),
        nameof(IMajorRecordGetter.VersionControl),
        nameof(ISkyrimMajorRecordGetter.FormVersion),
        nameof(ISkyrimMajorRecordGetter.Version2),
    ];

    /// <summary>The mask fields of the record flags, both views of one value: the parts take the flags bit by bit.</summary>
    public static readonly string[] FlagFields =
        [nameof(IMajorRecordGetter.MajorRecordFlagsRaw), nameof(ISkyrimMajorRecordGetter.SkyrimMajorRecordFlags)];

    /// <summary>The record flag behind <see cref="IMajorRecordGetter.IsCompressed"/>: how the record is stored, not what it holds.</summary>
    public const int CompressedBit = (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.Compressed;

    public const int DeletedBit = (int)SkyrimMajorRecord.SkyrimMajorRecordFlag.Deleted;
}
