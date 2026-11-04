namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

/// <summary>Why the cleaned mod's content where a moved object stands now still holds it.</summary>
public enum CloneNeed
{
    /// <summary>The cleaned mod's objects still stand in the area where it stands now.</summary>
    AreaStillStands,

    /// <summary>
    /// The area where it stands now does not keep it, but a left-behind step that is switched on measures it around one of the cleaned
    /// mod's objects that stands.
    /// </summary>
    NeighbourOfStandingObject,
}

/// <summary>Why another mod's object the cleaned mod moved may not be copied to where it stands now.</summary>
public enum CloneRefusal
{
    /// <summary>Its base is not resolved, not visible, or no plain scenery: a static that is no marker, a moveable static, or a tree or bush that cannot be harvested.</summary>
    NotPlainScenery,

    /// <summary>Its base has distant LOD, or the reference is visible when distant.</summary>
    HasDistantLod,

    /// <summary>The reference is persistent.</summary>
    Persistent,

    /// <summary>The reference stores more than its EditorID, base, placement and scale.</summary>
    HoldsMoreThanPlacement,

    /// <summary>
    /// As one of the cleaned mod's objects, the copy comes too close to an object that can cause removals once the restore is done,
    /// by the too-close settings of the cleaned mod's own objects.
    /// </summary>
    TooCloseAsCleanedModsObject,

    /// <summary>As one of the cleaned mod's objects, the copy stands on terrain the changed-terrain step finds changed, by the settings of the cleaned mod's own objects.</summary>
    OnChangedTerrainAsCleanedModsObject,
}

/// <summary>
/// Another mod's object the cleaned mod moved, whose old place goes back while the cleaned mod's content where it stands now still
/// holds it, and for which no copy exists yet: it goes back with a copy left where it stands now, unless the copy is refused.
/// </summary>
/// <param name="RefusedBy">Why it may not be copied; null when the copy is planned.</param>
/// <param name="Failure">Why the check named by <paramref name="RefusedBy"/> stopped unexpectedly, which refuses the copy too; null when it did not.</param>
public sealed record CloneJudged(CloneNeed Need, CloneRefusal? RefusedBy, string? Failure = null);
