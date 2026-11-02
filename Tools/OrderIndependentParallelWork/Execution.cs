namespace AddedObjectRemover;

/// <summary>How many workers parallel work may use.</summary>
public sealed record Execution
{
    public Execution(int workers)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1);
        Workers = workers;
    }

    public int Workers { get; }
}
