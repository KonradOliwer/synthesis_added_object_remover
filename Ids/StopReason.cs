namespace AddedObjectRemover;

public enum StopKind { NoTargetSet, CleanedModNotInLoadOrder, CleanedModDisabled, CleanedModLoadsAfterPatch, RequiringModLoadsAfterPatch, CleanedModNotLoadedBySynthesis, TargetIsThePatch, NoTargetObjects }

/// <param name="Patch">The patch plugin as the plugin list spells it.</param>
/// <param name="Mod">The plugin that loads after the patch: the cleaned mod, or a mod that requires it.</param>
/// <param name="ModPosition">The position of <paramref name="Mod"/> in the plugin list, counted from 0 as the mod manager's load order counts.</param>
/// <param name="PatchPosition">The patch's position in the same list; lower than <paramref name="ModPosition"/>.</param>
public sealed record PatchPlacement(string Patch, string Mod, int ModPosition, int PatchPosition);

/// <param name="Name">The target plugin as the settings name it; null when there is none to name.</param>
/// <param name="Placement">
/// Where the plugin that loads after the patch and the patch stand in the plugin list; set only for
/// <see cref="StopKind.CleanedModLoadsAfterPatch"/> and <see cref="StopKind.RequiringModLoadsAfterPatch"/>.
/// </param>
public sealed record StopReason(StopKind Kind, string? Name, PatchPlacement? Placement = null);
