namespace AddedObjectRemover.Steps.FitClashingObjects.Contracts;

/// <summary>The part of the fit step that records its unexpected errors.</summary>
public static class FitParts
{
    private const string Owner = "FitClashingObjects";

    /// <summary>Checking one target object at another placement; the subject is the object.</summary>
    public static readonly PartKey FitCheck = new(Owner, nameof(FitCheck));
}
