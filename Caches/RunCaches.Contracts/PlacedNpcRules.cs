using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Caches.RunCaches.Contracts;

/// <summary>How the placed-NPC index (C10) picks, places and counts the NPCs; the too-close step supplies the rules.</summary>
/// <param name="IsPlacedNpc">Whether an object of a space is a placed NPC.</param>
/// <param name="BodyTransform">Where an NPC's body stands.</param>
/// <param name="CountSizes">Counts the bodies of the placed NPCs by where their size came from, given the number of placed NPCs without a body.</param>
public sealed record PlacedNpcRules(
    Func<OtherObject, bool> IsPlacedNpc,
    Func<OtherObject, PlacedTransform> BodyTransform,
    Func<IReadOnlyCollection<NpcBodySet>, int, NpcSizeCounts> CountSizes);
