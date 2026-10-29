using AddedObjectRemover.Steps.FitClashingObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Run.RunSettingsValidation.Contracts;

/// <summary>
/// The collision handling of other mods' objects the cleaned mod changed, as the settings give it. Each kind of changed object
/// is judged with the same too-close and changed-terrain settings and is fitted only when its own resizing switch is on.
/// </summary>
/// <param name="ResizeLimits">The limits for shrinking or turning, used for the kinds whose switch is on.</param>
/// <param name="ResizeMoved">Whether a moved object is tried shrunk or turned before it is removed.</param>
/// <param name="ResizeReplaced">Whether a replaced object is tried shrunk or turned before it is removed.</param>
public sealed record ChangedObjectCollision(
    TooCloseSettings TooClose, ChangedTerrainSettings ChangedTerrain, FitSettings ResizeLimits, bool ResizeMoved, bool ResizeReplaced)
{
    /// <summary>The settings as typed: the limits whether or not a kind uses them.</summary>
    public CollisionSettings Section() => new(TooClose, ChangedTerrain, ResizeLimits);

    public CollisionSettings ForMoved() => Section() with { Fit = ResizeMoved ? ResizeLimits : FitSettings.Off };

    public CollisionSettings ForReplaced() => Section() with { Fit = ResizeReplaced ? ResizeLimits : FitSettings.Off };
}
