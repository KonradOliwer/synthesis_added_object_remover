namespace AddedObjectRemover;

public static class Tolerant
{
    /// <summary>Inclusive "at least" that also accepts a value up to <paramref name="epsilon"/> below the threshold.</summary>
    public static bool AtLeast(float value, float threshold, float epsilon) => value >= threshold - epsilon;
}
