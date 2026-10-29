using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveVisibleObjectsLeftBehind.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>A target object (index into the scanned targets) with the reason a step gave for taking it away from where it stands.</summary>
public abstract record ObjectWithReason(int TargetIndex)
{
    /// <param name="ChangedBy">The plugin whose terrain change the game shows; null when an earlier patcher of the group made it.</param>
    public sealed record TerrainChanged(int TargetIndex, PluginName? ChangedBy, TerrainChangeKind Kind) : ObjectWithReason(TargetIndex);

    public sealed record TooClose(int TargetIndex, OtherObject TooCloseTo, TooCloseCause Cause) : ObjectWithReason(TargetIndex);

    public sealed record Touching(int TargetIndex, int TouchedTargetIndex) : ObjectWithReason(TargetIndex);

    /// <param name="LeftItsPlaceShare">Fraction of the object's support held by objects that left their place, removed or restored.</param>
    /// <param name="RestoredShare">The part of <paramref name="LeftItsPlaceShare"/> held by restored objects.</param>
    /// <param name="MainSupporter">The target that left its place and holds the largest share of its support.</param>
    public sealed record LostSupport(int TargetIndex, float LeftItsPlaceShare, float RestoredShare, int MainSupporter) : ObjectWithReason(TargetIndex);

    /// <param name="Touched">Another mod's object left without support, removed or pulled down, that the object touched where it stood.</param>
    public sealed record TouchingAnotherModsObject(int TargetIndex, OtherObject Touched) : ObjectWithReason(TargetIndex);

    /// <param name="LeftItsPlaceShare">Fraction of the object's support held by target objects that left their place, removed or restored, and other mods' objects taken away.</param>
    /// <param name="RestoredShare">The part of <paramref name="LeftItsPlaceShare"/> held by restored target objects.</param>
    /// <param name="MainSupporter">The other mod's object left without support, removed or pulled down, holding the largest share of its support.</param>
    public sealed record LostSupportWithAnotherModsObject(int TargetIndex, float LeftItsPlaceShare, float RestoredShare, OtherObject MainSupporter) : ObjectWithReason(TargetIndex);

    /// <summary>A visible target object whose surrounding visible target objects were removed.</summary>
    public sealed record VisibleLeftBehind(int TargetIndex, VisibleLeftBehindCheck Evaluation) : ObjectWithReason(TargetIndex);

    /// <summary>An invisible target object inside another mod's object or whose surrounding visible target objects were removed.</summary>
    public sealed record LeftBehind(int TargetIndex, LeftBehindCheck Evaluation) : ObjectWithReason(TargetIndex);

    /// <summary>A member of the linked group of a target object that left its place, removed or restored.</summary>
    /// <param name="LinkedToTargetIndex">The member whose removal or restoration took the group along.</param>
    public sealed record LinkedTo(int TargetIndex, int LinkedToTargetIndex) : ObjectWithReason(TargetIndex);
}
