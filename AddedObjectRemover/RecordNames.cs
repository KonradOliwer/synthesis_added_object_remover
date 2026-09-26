using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>Human-readable record labels for the log.</summary>
internal static class RecordNames
{
    public static string Describe(IMajorRecordGetter record) => Describe(record.FormKey, record.EditorID);

    public static string Describe(FormKey formKey, string? editorId) =>
        string.IsNullOrEmpty(editorId) ? formKey.ToString() : $"{editorId} [{formKey}]";

    /// <summary>"from Origin.esp", plus the winning plugin when a later plugin overrides the record.</summary>
    public static string DescribeOrigin(FormKey formKey, ModKey winningMod) =>
        winningMod == formKey.ModKey
            ? $"from {formKey.ModKey}"
            : $"from {formKey.ModKey} (winning override in {winningMod})";

    public static string DescribeSpace(IMajorRecordGetter spaceRecord) => spaceRecord switch
    {
        IWorldspaceGetter worldspace => $"worldspace {Describe(worldspace)}",
        ICellGetter cell => $"interior {Describe(cell)}",
        _ => Describe(spaceRecord),
    };

    public static string DescribeBase(BaseObjectShapeProvider shapes, BaseRef? baseRef)
    {
        if (baseRef is not { } reference) return "(none)";
        return shapes.ResolveBaseOrNull(reference) is { } record ? Describe(record) : reference.FormKey.ToString();
    }
}
