namespace AddedObjectRemover;

public static class DoubleSum
{
    /// <summary>The sum of the values added up in double precision, so the rounding of a float sum does not hide small values.</summary>
    public static double Of(IEnumerable<float> values) => values.Sum(value => (double)value);
}
