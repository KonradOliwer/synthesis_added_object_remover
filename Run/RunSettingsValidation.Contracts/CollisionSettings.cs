using System.Collections.Immutable;
using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Run.RunSettingsValidation.Contracts;

/// <summary>The settings that decide whether an object clashes where it stands, and what may be done to it before it is removed.</summary>
/// <param name="TooClose">Too close to other mods' objects.</param>
/// <param name="ChangedTerrain">On terrain changed by other mods.</param>
/// <param name="Fit">The limits for shrinking or turning an object to make it fit.</param>
public sealed record CollisionSettings(TooCloseSettings TooClose, ChangedTerrainSettings ChangedTerrain, FitSettings Fit);

/// <summary>Target objects judged with one set of collision settings, which each step takes as its own group.</summary>
public sealed record CollisionGroup(ImmutableArray<TargetId> Objects, CollisionSettings Settings)
{
    public ObjectsJudgedWith<TooCloseSettings> TooClose => new(Objects, Settings.TooClose);

    public ObjectsJudgedWith<ChangedTerrainSettings> ChangedTerrain => new(Objects, Settings.ChangedTerrain);

    public ObjectsJudgedWith<FitSettings> Fit => new(Objects, Settings.Fit);
}
