namespace AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

/// <summary>Work counts of the NPC-stuck search.</summary>
/// <param name="CoreTests">Body boxes tested against an object, combined boxes of several possible bodies included.</param>
/// <param name="Conflicts">Target objects found with an NPC stuck in them.</param>
/// <param name="ConflictsWithoutBlockingCollision">Of <paramref name="Conflicts"/>, those not counted because their mesh collision does not stop characters.</param>
public readonly record struct NpcWork(long PairsTested, long CoreTests, int Conflicts, int ConflictsWithoutBlockingCollision) : IWork<NpcWork>
{
    public static NpcWork Zero => default;

    public static NpcWork operator +(NpcWork a, NpcWork b) =>
        new(a.PairsTested + b.PairsTested, a.CoreTests + b.CoreTests, a.Conflicts + b.Conflicts, a.ConflictsWithoutBlockingCollision + b.ConflictsWithoutBlockingCollision);
}
