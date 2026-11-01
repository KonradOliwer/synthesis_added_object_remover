using System.Numerics;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>Random points and boxes near the origin.</summary>
internal static class RandomBoxes
{
    private const float Reach = 300f;
    private const float MostHalfSize = 120f;

    public static Vector3 Point(Random random) =>
        new(Offset(random), Offset(random), Offset(random) / 4);

    public static OrientedBox[] Some(Random random, int count) =>
        [.. Enumerable.Range(0, count).Select(_ => One(random))];

    /// <summary>Some box sizes are zero, so that directions without ground area occur.</summary>
    private static OrientedBox One(Random random)
    {
        var half = new Vector3(HalfSize(random), HalfSize(random), HalfSize(random));
        var position = new Vector3(Offset(random), Offset(random), Offset(random) / 4);
        return OrientedBox.FromLocal(new Box(-half, half), TestTargets.At(position, random.NextSingle() * MathF.Tau));
    }

    private static float HalfSize(Random random) => random.Next(5) == 0 ? 0f : random.NextSingle() * MostHalfSize;

    private static float Offset(Random random) => (random.NextSingle() * 2 - 1) * Reach;
}
