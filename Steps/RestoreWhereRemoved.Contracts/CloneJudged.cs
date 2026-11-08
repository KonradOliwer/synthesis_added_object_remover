namespace AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

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

    /// <summary>As one of the cleaned mod's objects, the copy stands on terrain the changed-terrain rule finds changed, by the settings of the cleaned mod's own objects.</summary>
    OnChangedTerrainAsCleanedModsObject,

    /// <summary>A triangle of a navmesh the cleaned mod changed that stays changed has its centre inside the copy's box, so actors walk on it.</summary>
    ActorsWalkOnIt,

    /// <summary>
    /// The follow-up of the placed records leaving takes along the object the copy stands for, as the cleaned mod's own
    /// objects are taken along, so the copy would stand where nothing holds it.
    /// </summary>
    TakenAwayByFollowUp,
}

/// <summary>
/// Another mod's object the cleaned mod moved, whose old place goes back while the cleaned mod's content where it stands now needs
/// it, and for which no copy exists yet: it goes back with a copy left where it stands now; when the copy is refused it goes back
/// without one.
/// </summary>
/// <param name="RefusedBy">Why it may not be copied; null when the copy is planned.</param>
/// <param name="Failure">Why the check named by <paramref name="RefusedBy"/> stopped on a user-data failure, which refuses the copy too; null when it did not.</param>
public sealed record CloneJudged(CloneRefusal? RefusedBy, string? Failure = null);
