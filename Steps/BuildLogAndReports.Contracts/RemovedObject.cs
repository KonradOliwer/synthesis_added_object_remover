using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>A target object to remove (index into the scanned targets).</summary>
public abstract record RemovedObject(int TargetIndex);

/// <param name="ChangedBy">The plugin whose terrain change the game shows; null when an earlier patcher of the group made it.</param>
public sealed record TerrainChangedRemoval(int TargetIndex, PluginName? ChangedBy, TerrainChangeKind Kind) : RemovedObject(TargetIndex);

public sealed record TooCloseRemoval(int TargetIndex, OtherObject TooCloseTo, TooCloseCause Cause) : RemovedObject(TargetIndex);

public sealed record TouchingRemoval(int TargetIndex, int TouchedTargetIndex) : RemovedObject(TargetIndex);

/// <param name="RemovedShare">Fraction of the object's support held by removed objects.</param>
/// <param name="MainRemovedSupporter">The removed target holding the largest share of its support.</param>
public sealed record SupportRemoval(int TargetIndex, float RemovedShare, int MainRemovedSupporter) : RemovedObject(TargetIndex);

/// <param name="Touched">Another mod's object left without support, removed or pulled down, that the object touched where it stood.</param>
public sealed record TouchingAnotherModsObjectRemoval(int TargetIndex, OtherObject Touched) : RemovedObject(TargetIndex);

/// <param name="RemovedShare">Fraction of the object's support held by removed objects and other mods' objects taken away.</param>
/// <param name="MainSupporter">The other mod's object left without support, removed or pulled down, holding the largest share of its support.</param>
public sealed record SupportWithAnotherModsObjectRemoval(int TargetIndex, float RemovedShare, OtherObject MainSupporter) : RemovedObject(TargetIndex);

/// <summary>An invisible target object inside another mod's object or whose surrounding visible target objects were removed.</summary>
public sealed record LeftBehindRemoval(int TargetIndex, LeftBehindCheck Evaluation) : RemovedObject(TargetIndex);

/// <summary>A member of the linked group of a removed target object.</summary>
/// <param name="LinkedToTargetIndex">The removed member whose removal took the group along.</param>
public sealed record LinkedRemoval(int TargetIndex, int LinkedToTargetIndex) : RemovedObject(TargetIndex);
