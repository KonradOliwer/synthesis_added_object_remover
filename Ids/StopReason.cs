namespace AddedObjectRemover;

public enum StopKind { NoTargetSet, CleanedModNotInLoadOrder, CleanedModDisabled, CleanedModLoadsAfterPatch, CleanedModNotLoadedBySynthesis, TargetIsThePatch, NoTargetObjects }

/// <param name="Patch">The patch plugin as the plugin list spells it.</param>
/// <param name="CleanedModPosition">The cleaned mod's position in the plugin list, counted from 0 as the mod manager's load order counts.</param>
/// <param name="PatchPosition">The patch's position in the same list; lower than <paramref name="CleanedModPosition"/>.</param>
public sealed record PatchPlacement(string Patch, int CleanedModPosition, int PatchPosition);

/// <param name="Name">The target plugin as the settings name it; null when there is none to name.</param>
/// <param name="Placement">Where the cleaned mod and the patch stand in the plugin list; set only for <see cref="StopKind.CleanedModLoadsAfterPatch"/>.</param>
public sealed record StopReason(StopKind Kind, string? Name, PatchPlacement? Placement = null);
