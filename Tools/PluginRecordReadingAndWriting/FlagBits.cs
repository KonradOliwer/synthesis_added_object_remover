namespace AddedObjectRemover;

/// <summary>Compares and copies chosen bits of a flags value.</summary>
internal static class FlagBits
{
    public static bool Differ(int one, int other, int bits) => ((one ^ other) & bits) != 0;

    /// <returns>The target with the chosen bits taken from the source.</returns>
    public static int CopyBits(int target, int source, int bits) => (target & ~bits) | (source & bits);
}
