namespace AddedObjectRemover;

/// <summary>What a direction around a point was judged to be; a cleared direction names what its gone boxes are.</summary>
public enum SectorState
{
    Empty,
    Kept,

    /// <summary>Cleared, and every gone box in it was removed.</summary>
    Removed,

    /// <summary>Cleared, and every gone box in it was restored.</summary>
    Restored,

    /// <summary>Cleared, with removed and restored boxes in it.</summary>
    RemovedAndRestored,
}
