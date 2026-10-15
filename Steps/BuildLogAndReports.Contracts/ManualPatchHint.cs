namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

public enum ManualPatchHintType
{
    RemovedMarker,
    KeptLinkedGroup,
    KeptForNonPlacedReference,
    KeptTeleportDoor,
}

/// <summary>A removed or kept target object whose surroundings may need a manual patch.</summary>
/// <param name="TargetIndex">The object; for a linked group its first member.</param>
public sealed record ManualPatchHint(ManualPatchHintType Type, int TargetIndex, string Detail);
