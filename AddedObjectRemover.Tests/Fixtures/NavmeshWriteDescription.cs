namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>What the patch writes for a navmesh group, as one line of text, so two plans can be compared.</summary>
internal static class NavmeshWriteDescription
{
    public static string Of(NavmeshGroupWrite? write) =>
        write is null
            ? "none"
            : $"{string.Join(",", write.Members.Select(member => $"{member.Navmesh}/{member.Undo}/{member.EntryCopied?.Plugin}/{member.EntryCopied?.Map}"))} "
              + $"{string.Join(",", write.DoorLinksCopied)} {string.Join(",", write.DoorLinksCleared)}";
}
