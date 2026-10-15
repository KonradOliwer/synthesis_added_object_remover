using AddedObjectRemover.NifFooterWorkaround;
using NiflySharp;
using NiflySharp.Blocks;

namespace AddedObjectRemover;

/// <param name="Index">Block index of the node used as the mesh root.</param>
/// <param name="LibraryRootIndex">Block index of NiflySharp's GetRootNode().</param>
internal readonly record struct NifRoot(int Index, int LibraryRootIndex)
{
    public bool DiffersFromLibraryRoot => Index != LibraryRootIndex;
}

/// <summary>
/// Picks the mesh's root node. The engine uses the first root block listed in the file footer;
/// NiflySharp's GetRootNode() only guesses (block 0 if it is a node, else the first node), so the
/// footer's first root wins when it is a node. Without a usable footer the guess is kept.
/// </summary>
internal static class NifRootFinder
{
    /// <summary>Null when the NIF has no node at all.</summary>
    public static NifRoot? Find(NifFile nif, byte[] data)
    {
        if (FindLibraryRootIndex(nif) is not { } libraryRootIndex) return null;
        var rootIndex = FindFooterRootNodeIndex(nif, data) ?? libraryRootIndex;
        return new NifRoot(rootIndex, libraryRootIndex);
    }

    private static int? FindLibraryRootIndex(NifFile nif) =>
        NiflyCalls.Call(() => nif.GetRootNode() is { } rootNode && nif.GetBlockIndex(rootNode, out var index) ? index : (int?)null);

    /// <summary>The footer indexes the header's blocks, so it only applies when NiflySharp loaded exactly those.</summary>
    private static int? FindFooterRootNodeIndex(NifFile nif, byte[] data)
    {
        if (NifFooterReader.Read(data) is not { RootBlockIndices: [var firstRoot, ..] } footer) return null;
        var blocks = nif.Blocks;
        var consistent = footer.BlockCount == blocks.Count && firstRoot >= 0 && firstRoot < blocks.Count;
        return consistent && blocks[firstRoot] is NiNode ? firstRoot : null;
    }
}
