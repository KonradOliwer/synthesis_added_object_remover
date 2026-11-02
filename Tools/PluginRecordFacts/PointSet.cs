using System.Numerics;

namespace AddedObjectRemover;

/// <summary>A set of a LAND record's grid points (row by row from the bottom, x fastest), as a fixed bitset. Immutable.</summary>
public sealed class PointSet : IEquatable<PointSet>
{
    private const int BitsPerWord = 64;
    private const int WordCount = (TerrainGrid.VertexCount + BitsPerWord - 1) / BitsPerWord;

    private readonly ulong[] _words;

    private PointSet(ulong[] words) => _words = words;

    public static PointSet Of(IEnumerable<int> points)
    {
        var words = new ulong[WordCount];
        foreach (var point in points)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(point);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(point, TerrainGrid.VertexCount);
            words[point / BitsPerWord] |= 1UL << (point % BitsPerWord);
        }
        return new PointSet(words);
    }

    public bool Contains(int point) => (_words[point / BitsPerWord] & (1UL << (point % BitsPerWord))) != 0;

    public int Count => _words.Sum(BitOperations.PopCount);

    public bool IsEmpty => _words.All(word => word == 0);

    /// <summary>In ascending order.</summary>
    public IEnumerable<int> Points => Enumerable.Range(0, TerrainGrid.VertexCount).Where(Contains);

    public bool Equals(PointSet? other) => other is not null && _words.AsSpan().SequenceEqual(other._words);

    public override bool Equals(object? obj) => Equals(obj as PointSet);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var word in _words) hash.Add(word);
        return hash.ToHashCode();
    }
}

/// <summary>A set of the four quarters of a cell's terrain, by <see cref="TerrainQuadrants.IndexOf"/>, as a bitset.</summary>
public readonly record struct QuarterSet(int Bits)
{
    public static QuarterSet Of(IEnumerable<int> quarters) => new(quarters.Aggregate(0, (bits, quarter) => bits | (1 << quarter)));

    public bool Contains(int quarter) => (Bits & (1 << quarter)) != 0;

    public bool IsEmpty => Bits == 0;

    /// <summary>In ascending order.</summary>
    public IEnumerable<int> Quarters => Enumerable.Range(0, TerrainQuadrants.Count).Where(Contains);
}
