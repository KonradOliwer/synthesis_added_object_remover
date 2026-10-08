using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

internal static class ObjectVisibilityText
{
    /// <summary>Why the object is invisible, e.g. "Lights" or "base not found"; "visible" otherwise.</summary>
    public static string Describe(ObjectVisibility visibility)
    {
        if (visibility.BaseMissing) return "base not found";
        if (visibility.EffectOnlyMesh) return "effect-only mesh";
        return visibility.Kind?.ToString() ?? "visible";
    }
}
