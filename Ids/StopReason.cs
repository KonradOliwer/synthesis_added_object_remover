namespace AddedObjectRemover;

public enum StopKind { NoTargetSet, InvalidTargetName, TargetNotInLoadOrder, NoTargetObjects }

/// <param name="Name">The target plugin as the settings name it; null when there is none to name.</param>
public sealed record StopReason(StopKind Kind, string? Name);
