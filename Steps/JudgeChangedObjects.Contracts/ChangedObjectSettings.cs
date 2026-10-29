namespace AddedObjectRemover.Steps.JudgeChangedObjects.Contracts;

/// <summary>What happens to another mod's object the cleaned mod changed when a removal step would remove it.</summary>
public enum RestoreMode
{
    /// <summary>It goes back to its earlier version, with no check.</summary>
    Restore,

    /// <summary>It goes back to its earlier version when that stands free at its old place; otherwise it is disabled.</summary>
    RestoreIfNoCollision,

    /// <summary>It is disabled, like the cleaned mod's own objects.</summary>
    Remove,
}

/// <param name="Moved">For an object the cleaned mod moved.</param>
/// <param name="Replaced">For an object the cleaned mod gave another model; used only for replaced objects the run collects.</param>
public sealed record ChangedObjectSettings(RestoreMode Moved, RestoreMode Replaced);
