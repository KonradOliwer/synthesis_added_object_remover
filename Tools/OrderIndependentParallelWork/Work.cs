namespace AddedObjectRemover;

public static class Work
{
    /// <returns>The sum of the work counts; <see cref="IWork{TSelf}.Zero"/> when there are none.</returns>
    public static T Sum<T>(IEnumerable<T> works)
        where T : IWork<T> =>
        works.Aggregate(T.Zero, (total, work) => total + work);
}
