namespace AddedObjectRemover.Steps.FitClashingObjects.Contracts;

/// <summary>Which objects that clash where they stand are tried shrunk or turned, and when. A moved replacement is always tried moved back.</summary>
public enum ShrinkOrTurnObjects
{
    /// <summary>Only moved replacements, and only after moving them back.</summary>
    ReplacementsAfterMovingBack,

    /// <summary>Only moved replacements, where the cleaned mod put them before moving them back, and again after.</summary>
    ReplacementsAtBothSpots,

    /// <summary>Moved replacements as with <see cref="ReplacementsAtBothSpots"/>, and the cleaned mod's own objects where they stand.</summary>
    AllObjects,
}
