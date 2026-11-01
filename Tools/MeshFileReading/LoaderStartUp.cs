namespace AddedObjectRemover;

/// <summary>How starting the mesh loader went; meshes are loaded in parallel only when it succeeded.</summary>
internal sealed record LoaderStartUp(bool Succeeded)
{
    public static LoaderStartUp Success() => new(true);

    /// <summary>The minimal file it saved and loaded again held no blocks.</summary>
    public static LoaderStartUp HeldNoBlocks() => new(false);
}
