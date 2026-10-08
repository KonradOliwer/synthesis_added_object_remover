namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>An other-mod record skipped because the target plugin overrides it; listed in the verbose log.</summary>
public readonly record struct OverriddenOtherRecord(RecordKey Key, string? EditorId, PluginName WinningMod);
