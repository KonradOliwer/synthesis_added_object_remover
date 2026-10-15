namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <summary>The part of the too-close step that records its unexpected errors.</summary>
public static class TooCloseParts
{
    private const string Owner = "RemoveTooCloseObjects";

    /// <summary>Checking one target object for being too close; the subject is the object.</summary>
    public static readonly PartKey TooCloseCheck = new(Owner, nameof(TooCloseCheck));
}
