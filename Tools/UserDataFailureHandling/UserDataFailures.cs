using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>The user-data failure channel of one run.</summary>
public sealed class UserDataFailures : IUserDataFailures
{
    private readonly Lock _lock = new();
    private readonly Dictionary<PartFailure, int> _sequenceOfFailures = [];
    private int _recorded;

    public ErrorMark Mark()
    {
        lock (_lock) return new ErrorMark(_recorded);
    }

    public void Add(PartFailure failure)
    {
        lock (_lock)
        {
            if (_sequenceOfFailures.TryAdd(failure, _recorded + 1)) _recorded++;
        }
    }

    public ImmutableArray<PartFailure> Since(ErrorMark mark)
    {
        lock (_lock) return [.. _sequenceOfFailures.Where(recorded => recorded.Value > mark.Sequence).Select(recorded => recorded.Key)];
    }
}
