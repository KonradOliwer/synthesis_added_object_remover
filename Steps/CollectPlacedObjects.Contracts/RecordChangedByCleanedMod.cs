namespace AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

/// <summary>
/// A placed record of another plugin whose parts the cleaned mod changed, of every kind and whether or not the game shows it, with
/// what goes back to its earlier version and what a later plugin changed again.
/// </summary>
/// <param name="SpaceKey">The worldspace or interior of the winning version; the earlier version stands in the same one.</param>
/// <param name="Cell">The cell whose child list holds the winning version; for an interior it is the space itself.</param>
/// <param name="WhereItIsNow">The winning version's placement, the scale normalized; null when it has no position or rotation.</param>
/// <param name="BaseNow">The winning version's base object; null when its base link is empty.</param>
/// <param name="ReferenceNow">What the winning version says about the reference itself.</param>
/// <param name="IsPersistent">The winning version's own Persistent flag is set.</param>
/// <param name="Earlier">The version just below the cleaned mod's, with the parts that go back to it and those left to later plugins.</param>
/// <param name="Target">The target object the record is judged as; null when it is none.</param>
/// <param name="LinkedFromCleanedMod">A record the cleaned mod creates, not removed in the game, links to it.</param>
/// <param name="PresenceBefore">What the version just below the cleaned mod's says about whether the game has the record.</param>
/// <param name="DoorLink">
/// Its navmesh link, when its version just below the cleaned mod's or its winning version has one; null otherwise. The link is no
/// part of <see cref="EarlierVersion.Undo"/>.
/// </param>
/// <param name="InDeletedCell">A version read deletes the cell holding its winning version or the one holding its earlier version.</param>
/// <param name="PutThereBy">
/// The winning plugin when it is no patch (this group's or another's) and its position or base differs from the cleaned mod's, so it
/// put the object where it stands; null when the cleaned mod's place stands.
/// </param>
public sealed record RecordChangedByCleanedMod(
    RecordKey Key,
    string? EditorId,
    RecordKey SpaceKey,
    CellFact Cell,
    PluginName WinningMod,
    EulerPlacement? WhereItIsNow,
    BaseKey? BaseNow,
    ReferenceFacts ReferenceNow,
    bool IsPersistent,
    EarlierVersion Earlier,
    TargetId? Target,
    bool LinkedFromCleanedMod,
    PlacedPresence PresenceBefore,
    DoorLinkChange? DoorLink = null,
    bool InDeletedCell = false,
    PluginName? PutThereBy = null)
{
    /// <summary>Some part goes back to the earlier version.</summary>
    public bool HasPartsToUndo => Earlier.Undo != PlacedParts.None;

    /// <summary>The cleaned mod changed whether the game has the record, and its version just below the cleaned mod's is removed.</summary>
    /// <remarks>Its enable parent is not followed.</remarks>
    public bool RemovedBeforeTheCleanedMod => Earlier.Undo.HasFlag(PlacedParts.Presence) && PresenceBefore.Removed;

    /// <summary>The cleaned mod changed whether the game has the record, and its version just below the cleaned mod's is not removed.</summary>
    /// <remarks>Its enable parent is not followed.</remarks>
    public bool PresentBeforeTheCleanedMod => Earlier.Undo.HasFlag(PlacedParts.Presence) && !PresenceBefore.Removed;

    /// <summary>Some part was changed again by a later plugin or the patch.</summary>
    public bool HasPartsLeftToLaterPlugins => !Earlier.LeftToLaterPlugins.IsEmpty;
}
