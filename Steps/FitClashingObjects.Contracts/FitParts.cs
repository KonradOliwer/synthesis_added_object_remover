namespace AddedObjectRemover.Steps.FitClashingObjects.Contracts;

/// <summary>The parts of the fit step that record their unexpected errors.</summary>
public static class FitParts
{
    private const string Owner = "FitClashingObjects";

    /// <summary>Checking one target object shrunk or turned; the subject is the object.</summary>
    public static readonly PartKey FitCheck = new(Owner, nameof(FitCheck));

    /// <summary>Finding the clashing objects that others rest on; it fails as a whole.</summary>
    public static readonly PartKey RestingCheck = new(Owner, nameof(RestingCheck));
}
