namespace AddedObjectRemover;

/// <summary>How many workers a step's parallel work may use.</summary>
internal sealed record Execution
{
    public Execution(int workers)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1);
        Workers = workers;
    }

    public int Workers { get; }
}
