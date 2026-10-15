namespace AddedObjectRemover;

/// <summary>How starting the mesh loader went; meshes are loaded in parallel only when it succeeded.</summary>
/// <param name="Failure">The exception's type and message when the start-up threw; null otherwise.</param>
internal sealed record LoaderStartUp(bool Succeeded, string? Failure)
{
    public static LoaderStartUp Success() => new(true, null);

    /// <summary>The minimal file it saved and loaded again held no blocks.</summary>
    public static LoaderStartUp HeldNoBlocks() => new(false, null);

    public static LoaderStartUp Threw(string failure) => new(false, failure);
}
