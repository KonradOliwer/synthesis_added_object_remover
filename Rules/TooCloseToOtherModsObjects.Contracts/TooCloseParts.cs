namespace AddedObjectRemover.Rules.TooCloseToOtherModsObjects.Contracts;

/// <summary>The part of the too-close rule that records its unexpected errors.</summary>
public static class TooCloseParts
{
    /// <summary>The owner name the log and reports know these failures by.</summary>
    private const string Owner = "RemoveTooCloseObjects";

    /// <summary>Checking one target object for being too close; the subject is the object.</summary>
    public static readonly PartKey TooCloseCheck = new(Owner, nameof(TooCloseCheck));
}
