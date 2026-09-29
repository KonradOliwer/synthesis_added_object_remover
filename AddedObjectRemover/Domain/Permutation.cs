using System.Collections.Immutable;

namespace AddedObjectRemover;

internal static class Permutation
{
    /// <summary>Throws unless <paramref name="order"/> holds each of 0 to its length - 1 exactly once.</summary>
    public static void Require(ImmutableArray<int> order, string paramName)
    {
        var seen = new bool[order.Length];
        foreach (var item in order)
        {
            if ((uint)item >= (uint)seen.Length || seen[item])
                throw new ArgumentException($"Not a permutation of 0 to {order.Length - 1}: {item} is out of range or repeated.", paramName);
            seen[item] = true;
        }
    }
}
