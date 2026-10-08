namespace AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

public readonly record struct BoundsStats(
    int BasesFromNif,
    int BasesNifFallbackToObnd,
    int BasesFromObnd,
    int BasesWithoutBounds,
    int BasesUnresolved,
    int ModelsRead,
    int ModelsFailed,
    int ModelsEffectOnly,
    int ModelsFromLooseFiles,
    int ModelsFromArchives,
    int ModelsWithFooterRoot,
    int ArchivesIndexed,
    IReadOnlyList<KeyValuePair<string, int>> ModelFailuresByKind);
